namespace GodotSharpStringCacher;

public record class Config(bool ShortNames)
{
	public static readonly Config Default = new(false);
};
