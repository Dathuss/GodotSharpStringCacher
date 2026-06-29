namespace GodotSharpStringCacher.Console;

using Console = System.Console;

static class Program
{
	record class Params(string InFile, string OutFile, Config Config);

	static void PrintUsage()
	{
		Console.WriteLine($"Usage: {Environment.GetCommandLineArgs()[0]} <in_file> <out_file> [--long-names] [--no-warn-non-constant-implicit-operator]");
	}

	static Params? ParseParams(string[] args, LoggerBase log)
	{
		string? inFile = null;
		string? outFile = null;
		bool longNames = false;
		bool warnOnNonConstantImplicitOperator = true;

		foreach (string arg in args)
		{
			if (arg == "--long-names")
				longNames = true;
			else if (arg == "--no-warn-non-constant-implicit-operator")
				warnOnNonConstantImplicitOperator = false;
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
		return new Params(inFile, outFile, new Config(longNames, warnOnNonConstantImplicitOperator, log));
	}

	public static void Main(string[] args)
	{
		Logger log = new();

		try
		{
			Params? parameters = ParseParams(args, log);
			if (parameters is null)
				return;
			using Context ctx = new(parameters.Config);
			ctx.RunAndSave(parameters.InFile, parameters.OutFile);
		}
		catch (NoGodotSharpReferenceExeption ex)
		{
			log.LogError($"{ex}");
			Environment.Exit(1);
		}
		catch (IOException ex)
		{
			log.LogError($"An IO error occured: {ex}");
			Environment.Exit(1);
		}
		catch (Exception ex)
		{
			if (ex.InnerException is IOException)
				log.LogError($"An IO error occured: {ex}");
			else
				log.LogError($"An unhandled exception occured: {ex}");
			Environment.Exit(1);
		}
	}

	class Logger : LoggerBase
	{
		public override void LogMessage(string message)
		{
			Console.WriteLine(message);
		}

		public override void LogWarning(string message)
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine($"Warning: {message}");
			Console.ResetColor();
		}

		public override void LogError(string message)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Error.WriteLine($"Error: {message}");
			Console.ResetColor();
		}
	}
}
