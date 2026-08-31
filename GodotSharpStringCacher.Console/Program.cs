namespace GodotSharpStringCacher.Console;

using Console = System.Console;

static class Program
{
	record class Params(string InFile, string OutFile, string? GodotSharpPath, Config Config);

	static void PrintUsage()
	{
		Console.WriteLine($"Usage: {Environment.GetCommandLineArgs()[0]} <in_file> <out_file> [--long-names] [--godotsharp-path=PATH]");
	}

	static Params? ParseParams(string[] args)
	{
		string? inFile = null;
		string? outFile = null;
		string? godotSharpPath = null;
		bool longNames = false;

		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];
			if (arg == "--long-names")
				longNames = true;
			else if (arg.StartsWith("--godotsharp-path"))
			{
				int equalsIndex = arg.IndexOf('=');
				if (equalsIndex >= 0)
				{
					godotSharpPath = arg[(equalsIndex + 1)..];
				}
				else
				{
					if (i == args.Length - 1)
					{
						PrintUsage();
						return null;
					}
					godotSharpPath = args[++i];
				}
			}
			else if (inFile == null)
				inFile = arg;
			else if (outFile == null)
				outFile = arg;
			else
			{
				PrintUsage();
				return null;
			}
		}

		if (inFile == null || outFile == null)
		{
			PrintUsage();
			return null;
		}
		return new Params(inFile, outFile, godotSharpPath, new Config(longNames));
	}

	public static void Main(string[] args)
	{
		try
		{
			Params? parameters = ParseParams(args);
			if (parameters is null)
				return;
			using Context ctx = new(parameters.Config);
			if (parameters.GodotSharpPath != null)
				ctx.OpenGodotSharp(parameters.GodotSharpPath);
			ctx.RunAndSave(parameters.InFile, parameters.OutFile);
		}
		catch (NoGodotSharpReferenceExeption ex)
		{
			Console.Error.WriteLine(ex);
			Environment.Exit(1);
		}
		catch (IOException ex)
		{
			Console.Error.WriteLine($"An IO error occured: {ex}");
			Environment.Exit(1);
		}
		catch (Exception ex)
		{
			if (ex.InnerException is IOException)
				Console.Error.WriteLine($"An IO error occured: {ex}");
			else
				Console.Error.WriteLine($"An unhandled exception occured: {ex}");
			Environment.Exit(1);
		}
	}
}
