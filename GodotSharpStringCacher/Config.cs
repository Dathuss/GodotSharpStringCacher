namespace GodotSharpStringCacher;

public record class Config(bool UseLongNames, LoggerBase? Logger)
{
	public static readonly Config Default = new(false, null);
};
