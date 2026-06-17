using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace GodotSharpStringCacher;

internal class GodotSharpDefs
{
	public readonly ModuleDefinition Module;

	public readonly TypeReference StringNameType;
	public readonly IMethodDescriptor StringName_StringCtor;
	public readonly TypeReference NodePathType;
	public readonly IMethodDescriptor NodePath_StringCtor;

	static readonly Utf8String Utf8String_GodotSharp = "GodotSharp";

	public static GodotSharpDefs FromReferencingModule(ModuleDefinition module, IAssemblyResolver assemblyResolver)
	{
		AssemblyReference godotSharpRef = module.AssemblyReferences.FirstOrDefault(x => x.Name == Utf8String_GodotSharp) ?? throw new NoGodotSharpReferenceExeption(module);

		ResolutionStatus result = assemblyResolver.Resolve(godotSharpRef, module, out AssemblyDefinition? definition);

		if (result != ResolutionStatus.Success)
			throw new FileLoadException($"Could not load {godotSharpRef}: {result}");

		return new GodotSharpDefs(definition!.ManifestModule!);
	}
	
	private GodotSharpDefs(ModuleDefinition godotSharpModule)
	{
		Module = godotSharpModule;
		
		MethodSignature signature = MethodSignature.CreateInstance(Module.CorLibTypeFactory.Void, [Module.CorLibTypeFactory.String]);

		StringNameType = Module.CreateTypeReference("Godot", "StringName");
		StringName_StringCtor = StringNameType.CreateMethodReference(".ctor", signature);
		
		NodePathType = Module.CreateTypeReference("Godot", "NodePath");
		NodePath_StringCtor = NodePathType.CreateMethodReference(".ctor", signature);
	}
}
