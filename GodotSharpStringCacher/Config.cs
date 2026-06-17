namespace GodotSharpStringCacher;

public record class Config(bool UseLongNames, bool WarnOnNonConstantImplicitOperator, ILogger? Logger)
{
	public static readonly Config Default = new(false, true, null);
};
