using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace GodotSharpStringCacher;

public class Context : IDisposable
{
	public Config Config { get; set; }

	internal ModuleDefinition Module { get; private set; } = null!;

	internal string FileName { get; private set; } = null!;

	internal GodotSharpDefs? Defs { get; private set; } = null;

	internal string? GodotSharpDirectory { get; private set; } = null;

	internal TypeReference Imported_StringNameType { get; private set; } = null!;
	internal MethodReference Imported_StringName_StringCtor { get; private set; } = null!; 
	internal TypeReference Imported_NodePathType { get; private set; } = null!;
	internal MethodReference Imported_NodePath_StringCtor { get; private set; } = null!;

	internal readonly CacheTypesEmitter CacheTypesEmitter;

	public Context(Config? config = null)
	{
		Config = config ?? Config.Default;

		CacheTypesEmitter = new CacheTypesEmitter(this);
	}

	public int NumberOfStringNamesWritten { get; set; }
	public int NumberOfNodePathsWritten { get; set; }

	public void RunAndSave(string inputFile, string outputFile, out string? outputPdbFile)
	{
		FileName = inputFile;

		string directory = Path.GetDirectoryName(FileName) ?? throw new ArgumentException("Could not resolve directory name from module path");
		using DefaultAssemblyResolver resolver = new();
		if (GodotSharpDirectory != null)
			resolver.AddSearchDirectory(GodotSharpDirectory);
		resolver.AddSearchDirectory(directory);

		string tempDirectory, tempOutputFile;
		outputPdbFile = null;

		Module = ModuleDefinition.ReadModule(FileName, new ReaderParameters()
		{
			AssemblyResolver = resolver,
			ReadSymbols = true,
			ThrowIfSymbolsAreNotMatching = false,
			SymbolReaderProvider = new DefaultSymbolReaderProvider(throwIfNoSymbol: false)
		});
		using (Module)
		{
			if (Defs == null)
			{
				Defs = GodotSharpDefs.FromReferencingModule(Module, resolver);
				GodotSharpDirectory = Path.GetDirectoryName(Defs.Module.FileName);
			}
			ImportGodotSharpReferences();
			CacheTypesEmitter.Reset();

			foreach (TypeDefinition moduleType in Module.Types)
			{
				void PatchTypeAndNestedTypes(TypeDefinition type)
				{
					PatchType(type);
					foreach (TypeDefinition nestedType in type.NestedTypes)
					{
						PatchTypeAndNestedTypes(nestedType);
					}
				}
				PatchTypeAndNestedTypes(moduleType);
			}
			CacheTypesEmitter.EmitTypes();

			NumberOfStringNamesWritten = CacheTypesEmitter.StringNamesToCache.Count;
			NumberOfNodePathsWritten = CacheTypesEmitter.NodePathsToCache.Count;

			// Mono.Cecil will not behave correctly if you write to a module to itself
			// So we write it to a temp file first.

			// However, if a PDB file has to be emitted, it will be written relative to this temporary file too.
			// For example: for `tmp.qwerty.dll`, a PDB file named `tmp.qwerty.pdb` would be written.
			// A managed assembly holds the name of its associated PDB file, and in release builds,
			// this is the only file that the runtime will attempt to read.
			// This means we have to give the temporary file the same name as the output file,
			// so we put it in a temporary directory to give it the name we want without potentially overwriting another file.
			tempDirectory = CreateTempDir();
			tempOutputFile = Path.Combine(tempDirectory, Path.GetFileName(outputFile));
			if (Module.HasSymbols)
			{
				// Write DLL with optional PDB
				WriterParameters writerParameters = new()
				{
					WriteSymbols = true,
					SymbolWriterProvider = new DefaultSymbolWriterProvider(),
				};
				Module.Write(tempOutputFile, writerParameters);

				// Check if optional PDB was also written
				string cecilOutputPdb = GetPdbFileName(tempOutputFile);
				if (File.Exists(cecilOutputPdb))
				{
					// Move the optional PDB to the directory where the DLL will be moved to
					outputPdbFile = GetPdbFileName(outputFile);
					MoveFileWithOverwrite(cecilOutputPdb, outputPdbFile);
				}
			}
			else
			{
				// Write DLL without PDB (since no symbols are present)
				Module.Write(tempOutputFile);
			}
		}

		MoveFileWithOverwrite(tempOutputFile, outputFile);
		Directory.Delete(tempDirectory, recursive: true); // Directory should be empty, but delete recursively just to make sure
	}

	public void RunAndSave(string inputFile, string outputFile)
	{
		RunAndSave(inputFile, outputFile, out _);
	}

	static void MoveFileWithOverwrite(string sourceFile, string destFile)
	{
		// netstandard2.0 does not yet support the overwrite parameter in File.Move
		// So we have to do it manually.
		File.Delete(destFile);
		File.Move(sourceFile, destFile);
	}

	static string CreateTempDir()
	{
		string result;
		do
		{
			result = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		} while (Directory.Exists(result));

		Directory.CreateDirectory(result);
		return result;
	}

	public static string GetPdbFileName(string assemblyFileName)
	{
		return Path.ChangeExtension(assemblyFileName, ".pdb");
	}

	/// <summary>
	/// Manually open the GodotSharp assembly.
	/// </summary>
	public void OpenGodotSharp(string assemblyPath)
	{
		CloseGodotSharp();
		Defs = GodotSharpDefs.FromModule(ModuleDefinition.ReadModule(assemblyPath));
		GodotSharpDirectory = Path.GetDirectoryName(assemblyPath);
	}

	/// <summary>
	/// Closes the GodotSharp assembly, which allows to load a different GodotSharp assembly
	/// with the same Context.
	/// </summary>
	public void CloseGodotSharp()
	{
		Defs?.Dispose();
		Defs = null;
		GodotSharpDirectory = null;
	}

	void ImportGodotSharpReferences()
	{
		Imported_StringNameType = Module.ImportReference(Defs!.StringNameType);
		Imported_StringName_StringCtor = Module.ImportReference(Defs.StringName_StringCtor);
		Imported_NodePathType = Module.ImportReference(Defs.NodePathType);
		Imported_NodePath_StringCtor = Module.ImportReference(Defs.NodePath_StringCtor);
	}

	void PatchType(TypeDefinition type)
	{
		foreach (MethodDefinition typeMethod in type.Methods)
		{
			if (typeMethod.Body == null)
				continue;

			MatchAndPatch(typeMethod);
		}
	}

	void MatchAndPatch(MethodDefinition method)
	{
		Collection<Instruction> instructions = method.Body.Instructions;

		PatchSimpleFlowControl(instructions);

		PatchSequentialLdstrs(instructions, method);
	}

	/// <summary>
	/// Patches CIL patterns of the form
	/// <code>
	/// IL ldstr "MY_CONSTANT"
	/// IL call (Godot.StringName/Godot.NodePath)::op_Implicit(System.String)
	/// </code>
	/// To
	/// <code>
	/// ldsfld |our_generated_field|
	/// </code>
	/// 
	/// Requires <see cref="PatchSimpleFlowControl"/> to be run first.
	/// </summary>
	void PatchSequentialLdstrs(Collection<Instruction> instructions, MethodDefinition method)
	{
		for (int i = 1; i < instructions.Count; i++)
		{
			if (instructions[i] is not {OpCode.Code: Code.Call} callInstruction)
				continue;
			
			MethodReference calledMethod = (MethodReference)callInstruction.Operand;
			
			void TryMakeEdit(Func<string, FieldDefinition> fieldGetter, string typeName)
			{
				if (instructions[i - 1] is not {OpCode.Code: Code.Ldstr} ldstrInstruction)
				{
					if (Config.WarnOnNonConstantImplicitOperator && Config.Logger != null)
					{
						string warningMessage = $"{typeName} implicit operator with non-constant string found. Consider using 'new {typeName}' for clarity instead.";

						if (GetClosestSequencePoint(method.DebugInformation.SequencePoints, callInstruction) is SequencePoint sequencePoint)
						{
							Config.Logger.LogWarning(sequencePoint.Document.Url, sequencePoint.StartLine, sequencePoint.StartColumn, sequencePoint.EndLine, sequencePoint.EndColumn, warningMessage);
						}
						else
						{
							Config.Logger.LogWarning($"`{method}`: {warningMessage}");
						}
					}
					return;
				}
				ReplaceInstruction(ldstrInstruction, OpCodes.Ldsfld, fieldGetter((string)ldstrInstruction.Operand));
				ReplaceInstruction(callInstruction, OpCodes.Nop, null);
			}

			if (IsStringToStringNameImplicitOp(calledMethod))
			{
				TryMakeEdit(operand => CacheTypesEmitter.AddStringName(operand), "StringName");
			}
			else if (IsStringToNodePathImplicitOp(calledMethod))
			{
				TryMakeEdit(operand => CacheTypesEmitter.AddNodePath(operand), "NodePath");
			}
		}
	}

	/// <summary>
	/// The first patch. Targets branches that point to an implicit operator call.<br/>
	/// 
	/// This is important as <see cref="PatchSequentialLdstrs"/> would yield invalid CIL in the case of,
	/// for example a ternary operator of the form:
	/// <list type="bullet">
	///   <item><c>StringName x = GetBool() ? "abc" : "def"</c></item>
	///   <item><c>NodePath y = GetBool() ? "abc" : GetString()</c></item>
	/// </list>
	/// 
	/// The first example would yield CIL like this:
	/// <code>
	/// IL_01: call bool GetBool()
	/// IL_02: brtrue.s IL_05
	/// IL_03: ldstr "def"
	/// IL_04: br.s IL_06
	/// IL_05: ldstr "abc"
	/// IL_06: call class Godot.StringName Godot.StringName::op_Implicit(string)
	/// IL_07: (Rest of the function. At this point a single StringName was pushed to the stack.)
	/// </code>
	/// Notice how there is a single conversion call and both pathes flow into it.<br/>
	/// 
	/// <c>PatchSequentialLdstrs</c> would replace the <c>call</c> at <c>IL_06</c> with
	/// a <c>nop</c>, leaving <c>IL_04</c> to jump forward and leave a <c>string</c>
	/// on the stack where a <c>StringName</c> is expected.<br/>
	/// 
	/// This patch will ensure that if an unconditional branch is preceeded by a <c>ldstr</c>,
	/// and that the branch target is a conversion method, said <c>ldstr</c> will be cached
	/// and the branch will point to the instruction after the <c>call</c>.<br/>
	/// 
	/// Therefore, when <c>PatchSequentialLdstrs</c> runs, it will patch out the other path
	/// (or not, if the other path does not contain a constant string like in the second example)
	/// and CIL will be valid again.
	/// </summary>
	void PatchSimpleFlowControl(Collection<Instruction> instructions)
	{
		for (int i = 1; i < instructions.Count; i++)
		{
			if (instructions[i] is not
				{
					OpCode.FlowControl: FlowControl.Branch,
					Operand: Instruction
					{
						OpCode.Code: Code.Call,
						Operand: MethodReference calledMethod
					} callInstruction
				} branchInstruction)
			{
				continue;
			}
			
			void TryPatchBranch(Func<string, FieldDefinition> fieldGetter)
			{
				if (instructions[i - 1] is {OpCode.Code: Code.Ldstr} ldstrInstruction)
				{
					Config.Logger?.LogWarning($"replacing {ldstrInstruction}");
					ReplaceInstruction(ldstrInstruction, OpCodes.Ldsfld, fieldGetter((string)ldstrInstruction.Operand));
					// Point the branch to the next instruction, because if the other path does not
					// get its `call` removed, it will create invalid CIL.
					branchInstruction.Operand = callInstruction.Next;
				}
				else if (callInstruction.Previous.OpCode == OpCodes.Ldstr)
				{
					// In this case, PatchSequentialLdstrs will patch out the call. Therefore we insert a
					// call to the implicit operator before the branch and move the branch to the next
					// instruction.
					// It would be better if the user added an explicit constructor, but it's not a
					// reason to generate invalid CIL.
					Config.Logger?.LogWarning($"prepending to {branchInstruction}");
					instructions.Insert(i, Instruction.Create(OpCodes.Call, calledMethod));
					branchInstruction.Operand = callInstruction.Next;
					i++;
				}
			}

			if (IsStringToStringNameImplicitOp(calledMethod))
			{
				TryPatchBranch(CacheTypesEmitter.AddStringName);
			}
			else if (IsStringToNodePathImplicitOp(calledMethod))
			{
				TryPatchBranch(CacheTypesEmitter.AddNodePath);
			}
		}
	}

	void ReplaceInstruction(Instruction instruction, OpCode opCode, object? operand)
	{
		// Mono.Cecil has an oversight where if you replace an instruction, branches that point
		// to the previous Instruction object are not updated. This will lead to the corruption of the
		// method body when rebuilding the assembly
		// The easiest and fastest way to circumvent this is to directly edit the fields
		// of the Instruction object so as not to invalidate the reference.
		instruction.OpCode = opCode;
		instruction.Operand = operand;
	}

	/// <summary>
	/// Gets the nearest sequence point (AKA a marker of a location in a source file)
	/// from the given instruction. Looks for a sequence point upwards.
	/// </summary>
	/// <returns>The closest sequence point, <c>null</c> if none was found.</returns>
	SequencePoint? GetClosestSequencePoint(Collection<SequencePoint>? sequencePoints, Instruction instruction)
	{
		if (sequencePoints == null)
		{
			return null;
		}

		SequencePoint? closest = null;
		int currentClosestDistance = int.MaxValue;
		int instructionOffset = instruction.Offset;

		foreach (SequencePoint sequencePoint in sequencePoints)
		{
			if (sequencePoint.Offset == instructionOffset)
			{
				return sequencePoint;
			}
			int diff = instructionOffset - sequencePoint.Offset;
			if (diff > 0 && diff < currentClosestDistance)
			{
				currentClosestDistance = diff;
				closest = sequencePoint;
			}
		}

		return closest;
	}

	static bool IsStringToStringNameImplicitOp(MethodReference method)
	{
		return method.Name == "op_Implicit"
			&& method.DeclaringType.FullName == "Godot.StringName"
			&& method.ReturnType.FullName == "Godot.StringName"
			&& method.Parameters.Count == 1
			&& method.Parameters[0].ParameterType.FullName == "System.String";
	}

	static bool IsStringToNodePathImplicitOp(MethodReference method)
	{
		return method.Name == "op_Implicit"
			&& method.DeclaringType.FullName == "Godot.NodePath"
			&& method.ReturnType.FullName == "Godot.NodePath"
			&& method.Parameters.Count == 1
			&& method.Parameters[0].ParameterType.FullName == "System.String";
	}

	public void Dispose()
	{
		Defs?.Dispose();
	}
}
