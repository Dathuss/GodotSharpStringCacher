using Mono.Cecil;

namespace GodotSharpStringCacher;

internal class GodotSharpDefs
{
	public readonly ModuleDefinition Module;

	public readonly TypeDefinition StringNameType;
	public readonly MethodReference StringName_StringCtor;
	public readonly TypeDefinition NodePathType;
	public readonly MethodReference NodePath_StringCtor;

	public static GodotSharpDefs FromReferencingModule(ModuleDefinition module, IAssemblyResolver assemblyResolver)
	{
		AssemblyNameReference godotSharpRef = module.AssemblyReferences.FirstOrDefault(x => x.Name.Equals("GodotSharp", StringComparison.Ordinal)) ?? throw new NoGodotSharpReferenceExeption(module);

		AssemblyDefinition result = assemblyResolver.Resolve(godotSharpRef);

		return new GodotSharpDefs(result.MainModule);
	}
	
	private GodotSharpDefs(ModuleDefinition godotSharpModule)
	{
		Module = godotSharpModule;

		StringNameType = Module.GetType("Godot.StringName");
		NodePathType = Module.GetType("Godot.NodePath");
		
		StringName_StringCtor = StringNameType.Methods.First(x => 
			x.IsConstructor &&
			x.Parameters.Count == 1 &&
			x.Parameters[0].ParameterType.FullName == "System.String"
		);

		NodePath_StringCtor = NodePathType.Methods.First(x => 
			x.IsConstructor &&
			x.Parameters.Count == 1 &&
			x.Parameters[0].ParameterType.FullName == "System.String"
		);
	}
}
