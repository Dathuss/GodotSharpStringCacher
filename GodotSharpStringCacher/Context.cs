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

	// Only used inside MatchAndPatch, avoids unecessary allocation
	readonly List<(Instruction, FieldReference)> directConversionsToPatch = [];

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

		for (int i = 0; i < instructions.Count - 1; i++)
		{
			// We are looking for this pattern here:
			// IL ldstr "MY_CONSTANT"
			// IL call (Godot.StringName/Godot.NodePath)::op_Implicit(System.String)
			if (instructions[i] is {OpCode.Code: Code.Ldstr} ldstrInstruction)
			{
				if (instructions[i + 1] is not {OpCode.Code: Code.Call} callInstruction)
					continue;
				MethodReference calledMethod = (MethodReference)callInstruction.Operand;

				if (IsStringToStringNameImplicitOp(calledMethod))
				{
					directConversionsToPatch.Add((ldstrInstruction, CacheTypesEmitter.AddStringName((string)ldstrInstruction.Operand)));
				}
				else if (IsStringToNodePathImplicitOp(calledMethod))
				{
					directConversionsToPatch.Add((ldstrInstruction, CacheTypesEmitter.AddNodePath((string)ldstrInstruction.Operand)));
				}
			}
			/*
			 * However, patching this pattern would yield invalid CIL if branching is involved.
			 * For example `StringName x = GetBool() ? "abc" : "def";`
			 * would yield this CIL:
			 * IL_01: call bool GetBool()
			 * IL_02: brtrue.s IL_05

			 * IL_03: ldstr "def"
			 * IL_04: br.s IL_06

			 * IL_05: ldstr "abc"
			 * IL_06: call class Godot.StringName Godot.StringName::op_Implicit(string)
			 * IL_07: (Rest of the function. At this point a single StringName was pushed to the stack.)

			 * Notice how there is a single conversion call and both paths flow into it.

			 * The `call` at IL_06 would be patched out, and the "false" path at IL_03 would leave a
			 * string on the stack where a StringName is expected.

			 * We will ensure that if an unconditional branch is preceeded by a `ldstr`,
			 * and that the branch target is a conversion method, said `ldstr` will be cached
			 * and the branch will point to the instruction after the `call`.
			 */
			else if (instructions[i] is
				{
					OpCode.FlowControl: FlowControl.Branch,
					Operand: Instruction
					{
						OpCode.Code: Code.Call,
						Operand: MethodReference methodThatWillBeBranchedTo
					} pointedCallInstruction
				} branchInstruction)
			{
				if (IsStringToStringNameImplicitOp(methodThatWillBeBranchedTo))
				{
					TryPatchBranch(CacheTypesEmitter.AddStringName);
				}
				else if (IsStringToNodePathImplicitOp(methodThatWillBeBranchedTo))
				{
					TryPatchBranch(CacheTypesEmitter.AddNodePath);
				}

				void TryPatchBranch(Func<string, FieldDefinition> fieldGetter)
				{
					Instruction instBeforeTheBranch = instructions[i - 1];
					if (instBeforeTheBranch is {OpCode.Code: Code.Ldstr})
					{
						ReplaceInstruction(instBeforeTheBranch, OpCodes.Ldsfld, fieldGetter((string)instBeforeTheBranch.Operand));
						// Point the branch to the instruction that follows the `call op_Implicit`
						branchInstruction.Operand = pointedCallInstruction.Next;
					}
					else if (pointedCallInstruction.Previous.OpCode == OpCodes.Ldstr)
					{
						// If a `call op_Implicit` is preceded by a `ldstr`, it will be patched out.
						// We will therefore keep the conversion in this path
						// by inserting it before the branch.
						instructions.Insert(i, Instruction.Create(OpCodes.Call, methodThatWillBeBranchedTo));
						branchInstruction.Operand = pointedCallInstruction.Next;
						i++;
					}
				}
			}
		}

		foreach ((Instruction instruction, FieldReference field) in directConversionsToPatch)
		{
			ReplaceInstruction(instruction, OpCodes.Ldsfld, field);
			// Patch out the call instruction that follows
			ReplaceInstruction(instruction.Next, OpCodes.Nop, null);
		}
		directConversionsToPatch.Clear();
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
