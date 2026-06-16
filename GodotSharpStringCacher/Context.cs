using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
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
    internal readonly IMethodDefOrRef Imported_StringName_StringCtor; 
    internal readonly ITypeDefOrRef Imported_NodePathType;
    internal readonly IMethodDefOrRef Imported_NodePath_StringCtor;

    internal readonly CacheTypesEmitter CacheTypesEmitter;

    public Context(string fileName, Config? config = null)
    {
        Config = config ?? Config.Default;
        FileName = fileName;
        Module = ModuleDefinition.FromFile(FileName);

        string directory = Path.GetDirectoryName(FileName) ?? throw new ArgumentException("Could not resolve directory name from module path");
        PathAssemblyResolver resolver = PathAssemblyResolver.FromSearchDirectories([directory]);

        Defs = GodotSharpDefs.FromReferencingModule(Module, resolver);
        Imported_StringNameType = Module.DefaultImporter.ImportType(Defs.StringNameType);
        Imported_StringName_StringCtor = Module.DefaultImporter.ImportMethod(Defs.StringName_StringCtor);
        Imported_NodePathType = Module.DefaultImporter.ImportType(Defs.NodePathType);
        Imported_NodePath_StringCtor = Module.DefaultImporter.ImportMethod(Defs.NodePath_StringCtor);
        CacheTypesEmitter = new CacheTypesEmitter(this);
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

        foreach (TypeDefinition moduleType in Module.GetAllTypes())
        {
            PatchType(moduleType);
        }
        CacheTypesEmitter.EmitTypes();

        HasRun = true;
    }

    void PatchType(TypeDefinition type)
    {
        foreach (MethodDefinition typeMethod in type.Methods)
        {
            if (typeMethod.Signature == null || typeMethod.CilMethodBody == null)
                continue;

            // No need to patch if we're already in a static constructor
            if (typeMethod.Name != ".cctor")
                MatchAndPatch(typeMethod);
        }
    }

    void MatchAndPatch(MethodDefinition method)
    {
        CilInstructionCollection instructions = method.CilMethodBody!.Instructions;
        instructions.ExpandMacros();

        // We are looking for this pattern:
        // IL ldstr "MY_CONSTANT"
        // IL call (Godot.StringName/Godot.NodePath)::op_Implicit(System.String)

        // Which we will replace with
        // IL ldsfld our_generated_field

        for (int i = 0; i < instructions.Count - 1; i++)
        {
            CilInstruction ldstrInstruction = instructions[i];
            if (ldstrInstruction.OpCode != CilOpCodes.Ldstr)
                continue;

            CilInstruction callInstruction = instructions[i + 1];
            if (callInstruction.OpCode != CilOpCodes.Call)
                continue;

            if (callInstruction.Operand is not MemberReference calledMethod)
                continue;
            
            if (IsStringToStringNameImplicitOp(calledMethod))
            {
                FieldDefinition field = CacheTypesEmitter.AddStringName((string)ldstrInstruction.Operand!);

                instructions[i].ReplaceWith(CilOpCodes.Ldsfld, field);
                instructions.RemoveAt(i + 1);
            }
            else if (IsStringToNodePathImplicitOp(calledMethod))
            {
                FieldDefinition field = CacheTypesEmitter.AddNodePath((string)ldstrInstruction.Operand!);

                instructions[i].ReplaceWith(CilOpCodes.Ldsfld, field);
                instructions.RemoveAt(i + 1);
            }
        }

        instructions.OptimizeMacros();
    }

    static readonly Utf8String Utf8String_op_Implicit = "op_Implicit";

    static bool IsStringToStringNameImplicitOp(IMethodDefOrRef method)
    {
        return method.Name == Utf8String_op_Implicit &&
            method.DeclaringType?.FullName == "Godot.StringName" &&
            method.Signature!.ReturnType.FullName == "Godot.StringName" &&
            method.Signature.GetTotalParameterCount() == 1 &&
            method.Signature.ParameterTypes[0].FullName == "System.String";
    }

    static bool IsStringToNodePathImplicitOp(IMethodDefOrRef method)
    {
        return method.Name == Utf8String_op_Implicit &&
            method.DeclaringType?.FullName == "Godot.NodePath" &&
            method.Signature!.ReturnType.FullName == "Godot.NodePath" &&
            method.Signature.GetTotalParameterCount() == 1 &&
            method.Signature.ParameterTypes[0].FullName == "System.String";
    }
}
