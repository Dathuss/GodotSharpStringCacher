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

	public Context(Config? config = null)
	{
		Config = config ?? Config.Default;

		CacheTypesEmitter = new CacheTypesEmitter(this);
	}

	public int NumberOfStringNamesWritten { get; set; }
	public int NumberOfNodePathsWritten { get; set; }

	public void RunAndSave(string inputFile, string outputFile)
	{
		FileName = inputFile;

		string directory = Path.GetDirectoryName(FileName) ?? throw new ArgumentException("Could not resolve directory name from module path");
		using DefaultAssemblyResolver resolver = new();
		if (GodotSharpDirectory != null)
			resolver.AddSearchDirectory(GodotSharpDirectory);
		resolver.AddSearchDirectory(directory);

		string tempOutputFile;

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
			// So we write it to memory first, then overwrite the file.
			tempOutputFile = Path.GetTempFileName();
			Module.Write(tempOutputFile);
		}

		// Note: netstandard2.0 does not yet support the overwrite parameter in File.Move
		// So we delete it manually.
		File.Delete(outputFile);
		File.Move(tempOutputFile, outputFile);
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

			// No need to patch if we're already in a static constructor
			if (typeMethod.Name != ".cctor")
				MatchAndPatch(typeMethod);
		}
	}

	void MatchAndPatch(MethodDefinition method)
	{
		Collection<Instruction> instructions = method.Body.Instructions;
		bool hasSimplifiedMacros = false;

		// We are looking for this pattern:
		// IL ldstr "MY_CONSTANT"
		// IL call (Godot.StringName/Godot.NodePath)::op_Implicit(System.String)

		// Which we will replace with
		// IL ldsfld our_generated_field

		for (int i = 1; i < instructions.Count; i++)
		{
			if (instructions[i].OpCode != OpCodes.Call)
				continue;
			
			Instruction callInstruction = instructions[i];
			MethodReference calledMethod = (MethodReference)callInstruction.Operand;
			
			void TryMakeEdit(Func<string, FieldDefinition> fieldGetter, string typeName)
			{
				Instruction ldstrInstruction = instructions[i - 1];
				if (ldstrInstruction.OpCode != OpCodes.Ldstr)
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
				if (!hasSimplifiedMacros)
				{
					method.Body.SimplifyMacros();
					hasSimplifiedMacros = true;
				}
				// Mono.Cecil has a bug where if you replace an instruction, branches that point
				// to the previous Instruction object are not updated. This will lead to the corruption of the
				// method body when rebuilding the assembly
				// The easiest and fastest way to circumvent this is to directly edit the fields
				// of the Instruction object so as not to invalidate the reference.
				ldstrInstruction.OpCode = OpCodes.Ldsfld;
				ldstrInstruction.Operand = fieldGetter((string)ldstrInstruction.Operand);
				callInstruction.OpCode = OpCodes.Nop;
				callInstruction.Operand = null;
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

		if (hasSimplifiedMacros)
			method.Body.OptimizeMacros();
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
