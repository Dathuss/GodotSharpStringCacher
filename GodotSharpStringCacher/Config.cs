namespace GodotSharpStringCacher;

public record class Config(bool UseLongNames, ILogger? Logger)
{
	public static readonly Config Default = new(false, null);
};
