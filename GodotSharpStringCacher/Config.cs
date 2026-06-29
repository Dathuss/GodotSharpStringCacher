namespace GodotSharpStringCacher;

public record class Config(bool UseLongNames, bool WarnOnNonConstantImplicitOperator, LoggerBase? Logger)
{
	public static readonly Config Default = new(false, true, null);
};
