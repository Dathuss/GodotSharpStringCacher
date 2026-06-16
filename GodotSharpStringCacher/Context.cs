using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Cil;

namespace GodotSharpStringCacher;

public class Context
{
	public readonly ModuleDefinition Module;

	public readonly string FileName;

	public readonly Config Config;

	public bool HasRun { get; private set; }

	internal readonly GodotSharpDefs Defs;

	internal readonly ITypeDefOrRef Imported_StringNameType;
	internal readonly IMethodDescriptor Imported_StringName_StringCtor; 
	internal readonly ITypeDefOrRef Imported_NodePathType;
	internal readonly IMethodDescriptor Imported_NodePath_StringCtor;

	internal readonly CacheTypesEmitter CacheTypesEmitter;

	public Context(string fileName, Config? config = null)
	{
		Config = config ?? Config.Default;
		FileName = fileName;
		Module = ModuleDefinition.FromFile(FileName);

		var directory = Path.GetDirectoryName(FileName) ?? throw new ArgumentException("Could not resolve directory name from module path");
		var resolver = PathAssemblyResolver.FromSearchDirectories([directory]);

		Defs = GodotSharpDefs.FromReferencingModule(Module, resolver);
		Imported_StringNameType = Module.DefaultImporter.ImportType(Defs.StringNameType);
		Imported_StringName_StringCtor = Module.DefaultImporter.ImportMethod(Defs.StringName_StringCtor);
		Imported_NodePathType = Module.DefaultImporter.ImportType(Defs.NodePathType);
		Imported_NodePath_StringCtor = Module.DefaultImporter.ImportMethod(Defs.NodePath_StringCtor);
		CacheTypesEmitter = new(this);
	}

	public int NumberOfStringNamesWritten => CacheTypesEmitter.StringNamesToCache.Count;
	public int NumberOfNodePathsWritten => CacheTypesEmitter.NodePathsToCache.Count;

	public void Write(string fileName)
	{
		if (HasRun)
			Module.Write(fileName);
	}

	public void Run()
	{
		if (HasRun)
			return;
		foreach (var moduleType in Module.GetAllTypes())
		{
			PatchType(moduleType);
		}
		CacheTypesEmitter.EmitTypes();
		HasRun = true;
	}

	void PatchType(TypeDefinition type)
	{
		foreach (var typeMethod in type.Methods.Where(x => x.Signature != null && x.CilMethodBody != null))
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

		bool Match(int index)
		{
			// Poor man's code matching
			return instructions[index].OpCode == CilOpCodes.Ldstr && instructions[index + 1].OpCode == CilOpCodes.Call;
		}

		for (int i = 0; i < instructions.Count - 1; i++)
		{
			if (!Match(i))
				continue;
			var ldstrInstruction = instructions[i];
			var callInstruction = instructions[i + 1];

			if (callInstruction.Operand is not MemberReference calledMethod)
				continue;
			
			void MakeEdit(FieldDefinition field)
			{
				if (!hasExpandedMacros)
				{
					instructions.ExpandMacros();
					hasExpandedMacros = true;
				}
				instructions[i].ReplaceWith(CilOpCodes.Ldsfld, field);
				instructions.RemoveAt(i + 1);
			}

			if (IsStringToStringNameImplicitOp(calledMethod))
			{
				var field = CacheTypesEmitter.AddStringName((string)ldstrInstruction.Operand!);
				MakeEdit(field);
			}
			else if (IsStringToNodePathImplicitOp(calledMethod))
			{
				var field = CacheTypesEmitter.AddNodePath((string)ldstrInstruction.Operand!);
				MakeEdit(field);
			}
		}

		if (hasExpandedMacros)
			instructions.OptimizeMacros();
	}

	static bool IsStringToStringNameImplicitOp(IMethodDefOrRef method)
	{
		return method.Name == "op_Implicit" &&
			method.DeclaringType?.FullName == "Godot.StringName" &&
			method.Signature!.ReturnType.FullName == "Godot.StringName" &&
			method.Signature.GetTotalParameterCount() == 1 &&
			method.Signature.ParameterTypes[0].FullName == "System.String";
	}

	static bool IsStringToNodePathImplicitOp(IMethodDefOrRef method)
	{
		return method.Name == "op_Implicit" &&
			method.DeclaringType?.FullName == "Godot.NodePath" &&
			method.Signature!.ReturnType.FullName == "Godot.NodePath" &&
			method.Signature.GetTotalParameterCount() == 1 &&
			method.Signature.ParameterTypes[0].FullName == "System.String";
	}
}
