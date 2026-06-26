### Automatically cache Godot Mono/C# StringName and NodePath without changing your code

[![NuGet](https://img.shields.io/nuget/v/GodotSharpStringCacher.MSBuild.svg)](https://www.nuget.org/packages/GodotSharpStringCacher.MSBuild)

# What ?

Godot uses its own string types for a lot of features in the engine: `StringName` and `NodePath`. They each have their advantage which make a speed difference for the engine.  
However in C# they are often implicited declared and can have performance impact.  
For example, when writing something like `Input.IsActionJustPressed("my_input")`, the constructor to StringName is called on `"my_input"` without you noticing. 
This results in the GC allocation of an object that will live for only one method.
Worse, the implementation of these constructors does potentially expensive calculations (for StringName, a hash is computed, which is then searched in a linked list. For NodePath, the string is deconstructed in its individual nodes, and a list is allocated to cache the results)

There are different proposals to circumvent this issue, here is mine.

# Integrating

Include the package in your csproj file with
```xml
<PackageReference Include="GodotSharpStringCacher.MSBuild" PrivateAssets="all" />
```

That's it ! The main assembly is now automatically patched.  
If you want it to affect other packages, use
```xml
<ItemGroup>
    <PackageReference Include="Foo.Bar" CacheStrings="true" />
    <Reference Include=".../Foo.Bar.dll" CacheStrings="true" />
    <ProjectReference Include=".../Foo.Bar.csproj" CacheStrings="true" />

    <!-- Or by assembly name -->
    <CacheStrings Include="Foo.Bar" />
</ItemGroup>
```

# Quick Testing

If you want to test without integrating it, compile `GodotSharpStringCacher.Console`. The syntax is:
```
GodotSharpStringCacher.Console <in_file> <out_file> [--long-names] [--no-warn-non-constant-implicit-operator]
```

You can browse the resulting assembly and see the results for yourself.

# Detailed Configuration

## Warning On Implicit Operator With Non Constant

You are encouraged from using the `new` syntax when creating a non-constant `StringName`/`NodePath` at run-time. For example
```csharp
// don't
StringName myStr = networkPacket.StringVariable;
// do
StringName myStr = new StringName(networkPacket.StringVariable);
```
This conveys your intentions better, and you will be warned by default if you don't do this.

To disable this warning (which is not recommended), either use `WarnOnNonConstantImplicitOperator=true` on a specific assembly or add a property like this for global effect:
```xml
<GDStringWarnOnNonConstantImplicitOperator>false</GDStringWarnOnNonConstantImplicitOperator>
```

## Long Names

By default, the fields created from your constant strings in the assembly have a short numeric name. However, in niche cases, like statically reversing your own project, you may want these fields to have names that resemble their original value. For example, `"my_input"` would create a field called `_my_input`.
To enable this in a specific assembly, you can add `LongNames=true`. To enable this globally, add
```xml
<GDStringUseLongNames>true</GDStringUseLongNames>
```

# How It Works

It executes after your project compiles and directly modifies the resulting assembly using [Mono.Cecil](https://www.nuget.org/packages/Mono.Cecil), a library for assembly exploring and editing.  
It looks for implicit conversions, and if the string is a constant, it adds its value as a `StringName`/`NodePath` field to a static class, then replaces the conversion with a direct read to this field.

# Bugs and contributing

The current release should be stable. If you find a bug please please **PLEASE** raise an issue.

Feel free to contribute too!

If you find this project useful, drop a :star: too! Thank you :>
