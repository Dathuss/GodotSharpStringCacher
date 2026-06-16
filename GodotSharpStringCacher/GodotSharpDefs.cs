using AsmResolver;
using AsmResolver.DotNet;

namespace GodotSharpStringCacher;

internal class GodotSharpDefs {

    public readonly ModuleDefinition Module;

    public readonly TypeDefinition StringNameType;
    public readonly MethodDefinition StringName_StringCtor;
    public readonly TypeDefinition NodePathType;
    public readonly MethodDefinition NodePath_StringCtor;

    private GodotSharpDefs(ModuleDefinition godotSharpModule)
    {
        Module = godotSharpModule;

        StringNameType = Module.TopLevelTypes.First(x => x.FullName == "Godot.StringName");
        StringName_StringCtor = StringNameType.Methods.First(x => 
            x.IsConstructor &&
            x.Parameters.Count == 1 &&
            x.Parameters[0].ParameterType.FullName == "System.String"
        );
        NodePathType = Module.TopLevelTypes.First(x => x.FullName == "Godot.NodePath");
        NodePath_StringCtor = NodePathType.Methods.First(x => 
            x.IsConstructor &&
            x.Parameters.Count == 1 &&
            x.Parameters[0].ParameterType.FullName == "System.String"
        );
    }

    static readonly Utf8String Utf8String_GodotSharp = "GodotSharp";

    public static GodotSharpDefs FromReferencingModule(ModuleDefinition module, IAssemblyResolver assemblyResolver)
    {
        AssemblyReference godotSharpRef = module.AssemblyReferences.FirstOrDefault(x => x.Name == Utf8String_GodotSharp)
            ?? throw new NoGodotSharpReferenceExeption(module);

        ResolutionStatus result = assemblyResolver.Resolve(godotSharpRef, module, out AssemblyDefinition? definition);

        if (result != ResolutionStatus.Success)
            throw new FileLoadException($"Could not load {godotSharpRef}: {result}");

        return new GodotSharpDefs(definition!.ManifestModule!);
    }
}
