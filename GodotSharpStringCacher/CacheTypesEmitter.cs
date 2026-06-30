using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GodotSharpStringCacher;

internal class CacheTypesEmitter(Context ctx)
{
	public const string STRING_NAME_CACHE_TYPE_NAME = "?_StringNameCache";
	public const string NODE_PATH_CACHE_TYPE_NAME = "?_NodePathCache";

	internal readonly Dictionary<string, FieldDefinition> StringNamesToCache = [];
	internal readonly Dictionary<string, FieldDefinition> NodePathsToCache = [];

	public void Reset()
	{
		StringNamesToCache.Clear();
		NodePathsToCache.Clear();
	}

	public FieldDefinition AddStringName(string value)
	{
		if (StringNamesToCache.TryGetValue(value, out FieldDefinition? fld))
			return fld;

		string fieldName = ctx.Config.UseLongNames ? GetFieldName(value, StringNamesToCache.Values) : $"_{StringNamesToCache.Count}";
		FieldDefinition field = new(fieldName, FieldAttributes.Public | FieldAttributes.Static, ctx.Imported_StringNameType);
		StringNamesToCache.Add(value, field);
		return field;
	}

	public FieldDefinition AddNodePath(string value)
	{
		if (NodePathsToCache.TryGetValue(value, out FieldDefinition? fld))
			return fld;
		
		string fieldName = ctx.Config.UseLongNames ? GetFieldName(value, NodePathsToCache.Values) : $"_{NodePathsToCache.Count}";
		FieldDefinition field = new(fieldName, FieldAttributes.Public | FieldAttributes.Static, ctx.Imported_NodePathType);
		NodePathsToCache.Add(value, field);
		return field;
	}

	/// <summary>
	/// Emits the static types that cache NodePath and StringName values
	/// </summary
	public void EmitTypes()
	{
		MethodReference generatedCodeAttributeCtor = ctx.Module.ImportReference(typeof(System.CodeDom.Compiler.GeneratedCodeAttribute).GetConstructor([typeof(string), typeof(string)]));
		CustomAttribute generatedCodeAttribute = new(generatedCodeAttributeCtor);
		CustomAttributeArgument toolArgument = new(ctx.Module.TypeSystem.String, nameof(GodotSharpStringCacher));
		CustomAttributeArgument versionArgument = new(ctx.Module.TypeSystem.String, typeof(Context).Assembly.GetName().Version.ToString());
		generatedCodeAttribute.ConstructorArguments.Add(toolArgument);
		generatedCodeAttribute.ConstructorArguments.Add(versionArgument);

		TypeDefinition EmitType(string name, Dictionary<string, FieldDefinition> namesToCache, MethodReference ctorMethod)
		{
			TypeDefinition type = new("", name, TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, ctx.Module.TypeSystem.Object);
			type.CustomAttributes.Add(generatedCodeAttribute);

			/*
				Note: `.cctor` is the name of a type's static constructor.
				Writing `static Foo bar = new Foo();` is syntax sugar for:
				
				```
				static Foo bar;
				
				static DeclaringClass()
				{
					bar = new Foo();
				}
				```
			*/
			MethodDefinition cctor = new(".cctor", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.RTSpecialName | MethodAttributes.SpecialName, ctx.Module.TypeSystem.Void);
			ILProcessor processor = cctor.Body.GetILProcessor();
			
			foreach (var kv in namesToCache)
			{
				string value = kv.Key;
				FieldDefinition field = kv.Value;
				type.Fields.Add(field);
				processor.Emit(OpCodes.Ldstr, value);
				processor.Emit(OpCodes.Newobj, ctorMethod);
				processor.Emit(OpCodes.Stsfld, field);
			}
			processor.Emit(OpCodes.Ret);
			type.Methods.Add(cctor);
			ctx.Module.Types.Add(type);

			return type;
		}

		if (StringNamesToCache.Count != 0)
			EmitType(STRING_NAME_CACHE_TYPE_NAME, StringNamesToCache, ctx.Imported_StringName_StringCtor);

		if (NodePathsToCache.Count != 0)
			EmitType(NODE_PATH_CACHE_TYPE_NAME, NodePathsToCache, ctx.Imported_NodePath_StringCtor);
	}
	
	/// <summary>
	/// Turns a string value to a CIL field name with a closely resembling name.
	/// This can help static analysis, but makes patching slower
	/// </summary>
	/// <param name="existingFields">Existing field names to check for duplicates</param>
	string GetFieldName(string value, ICollection<FieldDefinition> existingFields)
	{
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
