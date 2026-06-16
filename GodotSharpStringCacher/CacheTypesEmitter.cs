using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Cil;

namespace GodotSharpStringCacher;

internal class CacheTypesEmitter(Context ctx)
{
    public const string STRING_NAME_CACHE_TYPE_NAME = "?_StringNameCache";
    public const string NODE_PATH_CACHE_TYPE_NAME = "?_NodePathCache";

    readonly FieldSignature StringNameFieldSig = new(ctx.Imported_StringNameType.ToTypeSignature(ctx.Module.RuntimeContext));
    readonly FieldSignature NodePathFieldSig = new(ctx.Imported_NodePathType.ToTypeSignature(ctx.Module.RuntimeContext));

    internal readonly Dictionary<string, FieldDefinition> StringNamesToCache = [];
    internal readonly Dictionary<string, FieldDefinition> NodePathsToCache = [];

    public FieldDefinition AddStringName(string value)
    {
        if (StringNamesToCache.TryGetValue(value, out FieldDefinition? fld))
            return fld;
        
        string fieldName = GetFieldName(value, StringNamesToCache.Values);
        FieldDefinition field = new(fieldName, FieldAttributes.Public | FieldAttributes.Static, StringNameFieldSig);
        StringNamesToCache.Add(value, field);
        return field;
    }

    public FieldDefinition AddNodePath(string value)
    {
        if (NodePathsToCache.TryGetValue(value, out FieldDefinition? fld))
            return fld;
        
        string fieldName = GetFieldName(value, NodePathsToCache.Values);
        FieldDefinition field = new(fieldName, FieldAttributes.Public | FieldAttributes.Static, NodePathFieldSig);
        NodePathsToCache.Add(value, field);
        return field;
    }

    /// <summary>
    /// Emits the static types that cache NodePath and StringName values
    /// </summary
    public void EmitTypes()
    {
        ITypeDefOrRef objectType = ctx.Module.CorLibTypeFactory.Object.GetUnderlyingTypeDefOrRef();

        TypeDefinition EmitType(string name, Dictionary<string, FieldDefinition> namesToCache, IMethodDefOrRef ctorMethod)
        {
            TypeDefinition type = new(null, name, TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit)
            {
                BaseType = objectType
            };

            /*
                FYI .cctor is the name of a type's static constructor.
                Writing `static Foo bar = new Foo();` is syntaxic sugar for

                static Foo bar;

                static DeclaringClass()
                {
                    bar = new Foo();
                }
            */
            MethodDefinition cctor = new(".cctor", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.RuntimeSpecialName | MethodAttributes.SpecialName, new MethodSignature(CallingConventionAttributes.Default, ctx.Module.CorLibTypeFactory.Void.GetUnderlyingTypeDefOrRef().ToTypeSignature(ctx.Module.RuntimeContext), null))
            {
                CilMethodBody = new CilMethodBody()
            };
            CilInstructionCollection instructions = cctor.CilMethodBody.Instructions;

            foreach (KeyValuePair<string, FieldDefinition> kv in namesToCache)
            {
                string value = kv.Key;
                FieldDefinition field = kv.Value;

                type.Fields.Add(field);

                instructions.Add(CilOpCodes.Ldstr, value);
                instructions.Add(CilOpCodes.Newobj, ctorMethod);
                instructions.Add(CilOpCodes.Stsfld, field);
            }
            instructions.Add(CilOpCodes.Ret);
            type.Methods.Add(cctor);
            ctx.Module.TopLevelTypes.Add(type);

            return type;
        }

        if (StringNamesToCache.Count != 0)
            EmitType(STRING_NAME_CACHE_TYPE_NAME, StringNamesToCache, ctx.Imported_StringName_StringCtor);

        if (NodePathsToCache.Count != 0)
            EmitType(NODE_PATH_CACHE_TYPE_NAME, NodePathsToCache, ctx.Imported_NodePath_StringCtor);
    }
    
    /// <summary>
    /// Turns a string value to a CIL field name with a closely resembling name.
    /// This can help static analysis.
    /// If <c>ctx.Config.ShortNames</c> is set, return a short name with no meaning.
    /// </summary>
    /// <param name="existingFields">Existing field names to check for duplicates</param>
    string GetFieldName(string value, IReadOnlyCollection<FieldDefinition> existingFields)
    {
        if (ctx.Config.ShortNames)
            return $"_{existingFields.Count}";

        string sanitized = value.Replace(' ', '_');
        string attempt = $"_{sanitized}";
        int trailing = 0;
        while (existingFields.Any(x => x.Name == attempt))
        {
            attempt = $"_{sanitized}_{trailing}";
            trailing++;
        }
        return attempt;
    }
}
