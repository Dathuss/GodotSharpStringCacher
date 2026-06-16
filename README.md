### Automatically cache Godot Mono/C# StringName and NodePath without changing your code

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
<PackageReference Include="GodotSharpStringCacher.MSBuild" Version="1.0.0-alpha" />
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
GodotSharpStringCacher.Console <in_file> <out_file> [--short-names]
```

You can browse the resulting assembly and see the results for yourself. (`--short-names` is explained below)

# Short Names

By default, the variables created from your constant string in the assembly closely resemble the string. For example, `"my_input"` creates a variable called `_my_input`. This can help when statically reversing your own project.  
If you don't want that in a specific assembly, you can add `ShortNames=true`. If you don't want that globally, add  
```xml
<GDStringUseShortNames>true</GDStringUseShortNames>
```

# How It Works

It executes after your project compiles and directly edits the resulting assembly. It uses [AsmResolver](https://github.com/Washi1337/AsmResolver), a library for assembly exploring and editing.  
It looks for calls to these implicit conversion operators, then if the string is a constant, adds its value as a StringName/NodePath to a static class, then replaces the call with the load to this value.

# Bugs and contributing

I only tested this project on my own Godot projects, and it is still new. Therefore, there may be bugs. If you find a bug please please **PLEASE** fill an issue. Thanks :>


Feel free to contribute too !
