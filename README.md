### Automatically cache Godot Mono/C# StringName and NodePath without changing your code

[![NuGet](https://img.shields.io/nuget/v/GodotSharpStringCacher.MSBuild.svg)](https://www.nuget.org/packages/GodotSharpStringCacher.MSBuild)

(Make sure to upvote [the related proposal to merge into Godot](https://github.com/godotengine/godot-proposals/issues/15089), it would help a ton !)

# What?

Godot uses its own string types meant for specific uses: `StringName` and `NodePath`. `StringName` is optimized for fast equality comparisons and hash lookups, and `NodePath` is optimized for storing scene tree paths.

However, in C#, they are often implicitly converted from `string`, which results in performance penalties.

For example, when writing `Input.IsActionPressed("my_input")`, the string `"my_input"` is implicitly converted from `string` to `StringName`. This allocates a new `StringName`, which contributes to performance spikes from garbage collection.

To make it worse, these implicit conversions do expensive calculations. For `StringName`, a hash is computed, which is then searched in a linked list. For `NodePath`, the path is deconstructed to its individual parts, and a list is allocated to cache the results.

There are several proposals to circumvent this issue, but this one is "plug 'n' play" and has no extra runtime performance penalties.

# Integrating

Include the package in your csproj file with
```xml
<PackageReference Include="GodotSharpStringCacher.MSBuild" PrivateAssets="all" Version="1.1.0" />
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

`GodotSharpStringCacher` is also available as a library. Integrate it into other builds like this:

```xml
<PackageReference Include="GodotSharpStringCacher" Version="1.2.0" />
```
Then:
```csharp
using GodotSharpStringCacher;

...

using var ctx = new Context(config: new Config(...));
// (optional) open GodotSharp manually (otherwise it will be loaded if present in the same directory as the patching DLL)
ctx.OpenGodotSharp("/foo/GodotSharp.dll");
ctx.RunAndSave("input.dll", "output.dll");
// A PDB file may be emitted. If you need to check if it was created or want to access it, use the optional out parameter
ctx.RunAndSave("input2.dll", "output2.dll", out string? outputPdbFile);
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
