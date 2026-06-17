using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Cil;

namespace GodotSharpStringCacher;

public class Context
{
	internal ModuleDefinition Module { get; private set; } = null!;

	internal RuntimeContext RuntimeContext { get; private set; } = null!;

	internal string FileName { get; private set; } = null!;

	internal string? LastRunDirectory { get; private set; } = null;

	public Config Config { get; set; }

	internal GodotSharpDefs Defs { get; private set; } = null!;

	internal ITypeDefOrRef Imported_StringNameType { get; private set; } = null!;
	internal IMethodDescriptor Imported_StringName_StringCtor { get; private set; } = null!; 
	internal ITypeDefOrRef Imported_NodePathType { get; private set; } = null!;
	internal IMethodDescriptor Imported_NodePath_StringCtor { get; private set; } = null!;

	internal readonly CacheTypesEmitter CacheTypesEmitter;

	public Context(Config? config = null)
	{
		Config = config ?? Config.Default;

		CacheTypesEmitter = new(this);
	}

	public int NumberOfStringNamesWritten { get; set; }
	public int NumberOfNodePathsWritten { get; set; }

	public void RunAndSave(string inputFile, string outputFile)
	{
		FileName = inputFile;

		var directory = Path.GetDirectoryName(FileName) ?? throw new ArgumentException("Could not resolve directory name from module path");
		if (LastRunDirectory == null || LastRunDirectory != directory)
		{
			Module = ModuleDefinition.FromFile(FileName, createRuntimeContext: true);
			RuntimeContext = Module.RuntimeContext!;

			// since we are in a different directory, the GodotSharp assembly may not be the same, so we reload everything.
			var resolver = PathAssemblyResolver.FromSearchDirectories([directory]);

			Defs = GodotSharpDefs.FromReferencingModule(Module, resolver);
			Imported_StringNameType = Module.DefaultImporter.ImportType(Defs.StringNameType);
			Imported_StringName_StringCtor = Module.DefaultImporter.ImportMethod(Defs.StringName_StringCtor);
			Imported_NodePathType = Module.DefaultImporter.ImportType(Defs.NodePathType);
			Imported_NodePath_StringCtor = Module.DefaultImporter.ImportMethod(Defs.NodePath_StringCtor);
			
			CacheTypesEmitter.Reset(true);
			LastRunDirectory = directory;
		}
		else
		{
			Module = RuntimeContext.LoadAssembly(inputFile).ManifestModule ?? throw new NullReferenceException("ManifestModule is null");
			CacheTypesEmitter.Reset(false);
		}

		foreach (var moduleType in Module.GetAllTypes())
		{
			PatchType(moduleType);
		}
		CacheTypesEmitter.EmitTypes();
		Module.Write(outputFile);
		NumberOfStringNamesWritten = CacheTypesEmitter.StringNamesToCache.Count;
		NumberOfNodePathsWritten = CacheTypesEmitter.NodePathsToCache.Count;
	}

	void PatchType(TypeDefinition type)
	{
		foreach (var typeMethod in type.Methods.Where(x => x.CilMethodBody != null))
		{
			// No need to patch if we're already in a static constructor
			if (typeMethod.Name != ".cctor")
				MatchAndPatch(typeMethod);
		}
	}

	void MatchAndPatch(MethodDefinition method)
	{
		var instructions = method.CilMethodBody!.Instructions;
		bool hasExpandedMacros = false;

		// We are looking for this pattern:
		// IL ldstr "MY_CONSTANT"
		// IL call (Godot.StringName/Godot.NodePath)::op_Implicit(System.String)

		// Which we will replace with
		// IL ldsfld our_generated_field

		for (int i = 1; i < instructions.Count; i++)
		{
			if (instructions[i].OpCode != CilOpCodes.Call)
				continue;
			
			var callInstruction = instructions[i];

			if (callInstruction.Operand is not MemberReference calledMethod)
				continue;
			
			void TryMakeEdit(Func<string, FieldDefinition> fieldGetter, string typeName)
			{
				var ldstrInstruction = instructions[i - 1];
				if (ldstrInstruction.OpCode != CilOpCodes.Ldstr)
				{
					if (Config.WarnOnNonConstantImplicitOperator)
						Config.Logger?.LogWarning($"`{method}`: {typeName} implicit operator with non-constant string found. Consider using 'new StringName' for clarity instead.");
					return;
				}
				if (!hasExpandedMacros)
				{
					instructions.ExpandMacros();
					hasExpandedMacros = true;
				}
				ldstrInstruction.ReplaceWith(CilOpCodes.Ldsfld, fieldGetter((string)ldstrInstruction.Operand!));
				instructions.RemoveAt(i);
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

		if (hasExpandedMacros)
			instructions.OptimizeMacros();
	}

	static bool IsStringToStringNameImplicitOp(IMethodDefOrRef method)
	{
		return method.Name == "op_Implicit" &&
			string.CompareOrdinal(method.DeclaringType?.FullName, "Godot.StringName") == 0 &&
			string.CompareOrdinal(method.Signature!.ReturnType.FullName, "Godot.StringName") == 0 &&
			method.Signature.GetTotalParameterCount() == 1 &&
			string.CompareOrdinal(method.Signature.ParameterTypes[0].FullName, "System.String") == 0;
	}

	static bool IsStringToNodePathImplicitOp(IMethodDefOrRef method)
	{
		return method.Name == "op_Implicit" &&
			string.CompareOrdinal(method.DeclaringType?.FullName, "Godot.NodePath") == 0 &&
			string.CompareOrdinal(method.Signature!.ReturnType.FullName, "Godot.NodePath") == 0 &&
			method.Signature.GetTotalParameterCount() == 1 &&
			string.CompareOrdinal(method.Signature.ParameterTypes[0].FullName, "System.String") == 0;
	}
}
