namespace GodotSharpStringCacher;

public record class Config(bool UseLongNames)
{
	public static readonly Config Default = new(false);
};
