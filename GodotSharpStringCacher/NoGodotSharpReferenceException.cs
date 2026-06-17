using AsmResolver.DotNet;

namespace GodotSharpStringCacher;

public class NoGodotSharpReferenceExeption(ModuleDefinition module) : Exception
{
	public override string Message { get; } = $"Module {module} does not contain a reference to GodotSharp.";
}
