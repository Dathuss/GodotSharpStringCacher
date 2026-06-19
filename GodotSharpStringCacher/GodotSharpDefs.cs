using Mono.Cecil;

namespace GodotSharpStringCacher;

internal class GodotSharpDefs : IDisposable
{
	public readonly ModuleDefinition Module;

	public readonly TypeReference StringNameType;
	public readonly MethodReference StringName_StringCtor;
	public readonly TypeReference NodePathType;
	public readonly MethodReference NodePath_StringCtor;

	public static GodotSharpDefs FromReferencingModule(ModuleDefinition module, IAssemblyResolver assemblyResolver)
	{
		AssemblyNameReference godotSharpRef = module.AssemblyReferences.FirstOrDefault(x => x.Name == "GodotSharp") ?? throw new NoGodotSharpReferenceExeption(module);

		AssemblyDefinition result = assemblyResolver.Resolve(godotSharpRef);

		return new GodotSharpDefs(result.MainModule);
	}

	public static GodotSharpDefs FromModule(ModuleDefinition godotSharpModule)
	{
		return new(godotSharpModule);
	}

	private GodotSharpDefs(ModuleDefinition godotSharpModule)
	{
		Module = godotSharpModule;

		StringNameType = new TypeReference("Godot", "StringName", Module, Module);
		NodePathType = new TypeReference("Godot", "NodePath", Module, Module);

		ParameterDefinition stringParameter = new(Module.TypeSystem.String);

		StringName_StringCtor = new MethodReference(".ctor", Module.TypeSystem.Void, StringNameType)
		{
			HasThis = true
		};
		StringName_StringCtor.Parameters.Add(stringParameter);
		
		NodePath_StringCtor = new MethodReference(".ctor", Module.TypeSystem.Void, NodePathType)
		{
			HasThis = true
		};
		NodePath_StringCtor.Parameters.Add(stringParameter);
	}

	public void Dispose()
	{
		Module.Dispose();
	}
}
