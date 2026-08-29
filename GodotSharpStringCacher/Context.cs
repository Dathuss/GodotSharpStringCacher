using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
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

	readonly record struct LdstrToPatch(
		Instruction Ldstr,
		Func<string, FieldReference> FieldCacher,
		Instruction TargetOperatorCall);

	// Only used inside MatchAndPatch, avoids unecessary allocation
	readonly List<LdstrToPatch> ldstrsToPatch = [];
	readonly HashSet<Instruction> directConversionsThatCannotBeRemoved = [];

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
			if (instructions[i] is { OpCode.Code: Code.Ldstr } ldstrInstruction)
			{
				if (instructions[i + 1] is not { OpCode.Code: Code.Call } callInstruction)
					continue;
				MethodReference calledMethod = (MethodReference)callInstruction.Operand;

				if (IsStringToStringNameImplicitOp(calledMethod))
				{
					ldstrsToPatch.Add(new(ldstrInstruction, CacheTypesEmitter.AddStringName, callInstruction));
				}
				else if (IsStringToNodePathImplicitOp(calledMethod))
				{
					ldstrsToPatch.Add(new(ldstrInstruction, CacheTypesEmitter.AddNodePath, callInstruction));
				}
			}
			// We are looking for unconditional branches that point to a `call op_Implicit`.
			// Any `ldstr` that jumps to a `call op_Implicit` will be cached.
			// Additionally, we need to determine if this call can be safely removed.
			// It can safely be removed if all instructions that flow to it
			// (either directly or via an unconditional branch) are `ldstr`s.
			else if (instructions[i] is { OpCode.FlowControl: FlowControl.Branch } branchInstruction)
			{
				if (FollowBranch(branchInstruction) is
					{
						OpCode.Code: Code.Call,
						Operand: MethodReference methodThatWillBeBranchedTo
					} followedInstruction)
				{
					if (IsStringToStringNameImplicitOp(methodThatWillBeBranchedTo))
					{
						AnalyzeBranch(CacheTypesEmitter.AddStringName);
					}
					else if (IsStringToNodePathImplicitOp(methodThatWillBeBranchedTo))
					{
						AnalyzeBranch(CacheTypesEmitter.AddNodePath);
					}
				}

				void AnalyzeBranch(Func<string, FieldDefinition> fieldCacher)
				{
					Instruction instBeforeTheBranch = instructions[i - 1];

					if (instBeforeTheBranch.OpCode.Code == Code.Ldstr)
					{
						// Here, we have a `ldstr` followed by a branch to a `call op_Implicit`,
						// so we can patch the `ldstr` later.
						ldstrsToPatch.Add(new(instBeforeTheBranch, fieldCacher, followedInstruction));

						if (followedInstruction.Previous.OpCode != OpCodes.Ldstr)
						{
							// Here, the `call op_Implicit` is not directly preceded by a `ldstr`,
							// so the `call op_Implicit` cannot be safely removed.
							directConversionsThatCannotBeRemoved.Add(followedInstruction);
						}
					}
					else
					{
						// Here, we have a non-constant string followed by a branch to a `call op_Implicit`,
						// so the `call op_Implicit` cannot be safely removed.
						directConversionsThatCannotBeRemoved.Add(followedInstruction);
					}
				}
			}
		}

		if (directConversionsThatCannotBeRemoved.Count == 0)
		{
			// Most functions are in this case
			foreach (LdstrToPatch entry in ldstrsToPatch)
			{
				// Replace the `load string` instruction with a `load field` instruction
				ReplaceInstruction(entry.Ldstr, OpCodes.Ldsfld, entry.FieldCacher((string)entry.Ldstr.Operand));
				// Replace the `call op_Implicit` instruction with a `no-op` instruction
				ReplaceInstruction(entry.TargetOperatorCall, OpCodes.Nop, null);
			}
		}
		else
		{
			method.Body.SimplifyMacros();
			foreach (LdstrToPatch entry in ldstrsToPatch)
			{
				// Replace the `load string` instruction with a `load field` instruction
				ReplaceInstruction(entry.Ldstr, OpCodes.Ldsfld, entry.FieldCacher((string)entry.Ldstr.Operand));

				if (directConversionsThatCannotBeRemoved.Contains(entry.TargetOperatorCall))
				{
					// Insert a branch after the `load field` instruction
					// to jump over the `call op_Implicit`
					instructions.Insert(
						instructions.IndexOf(entry.Ldstr) + 1,
						Instruction.Create(OpCodes.Br, entry.TargetOperatorCall.Next));
				}
				else
				{
					// Safely replace the `call op_Implicit` instruction with a `no-op` instruction
					ReplaceInstruction(entry.TargetOperatorCall, OpCodes.Nop, null);
				}
			}
			method.Body.OptimizeMacros();
			directConversionsThatCannotBeRemoved.Clear();
		}
		ldstrsToPatch.Clear();
	}

	/// <summary>
	/// Follow unconditional branches until a different instruction is found.
	/// </summary>
	Instruction FollowBranch(Instruction branchInstruction)
	{
		Instruction result = branchInstruction;
		do
			result = (Instruction)result.Operand;
		while (result.OpCode.FlowControl == FlowControl.Branch);
		return result;
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
