namespace GodotSharpStringCacher.Console;

using Console = System.Console;

static class Program
{
	record class Params(string InFile, string OutFile, Config Config);

	static void PrintUsage()
	{
		Console.WriteLine($"Usage: {Environment.GetCommandLineArgs()[0]} <in_file> <out_file> [--long-names] [--no-warn-non-constant-implicit-operator]");
	}

	static Params? ParseParams(string[] args)
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
		return new Params(inFile, outFile, new Config(longNames, warnOnNonConstantImplicitOperator, new SimpleLogger()));
	}

	public static void Main(string[] args)
	{
		try
		{
			Params? parameters = ParseParams(args);
			if (parameters is null)
				return;
			Context ctx = new(parameters.Config);
			ctx.RunAndSave(parameters.InFile, parameters.OutFile);
		}
		catch (NoGodotSharpReferenceExeption ex)
		{
			Console.Error.WriteLine(ex.Message);
			Environment.Exit(1);
		}
		catch (IOException ex)
		{
			Console.Error.WriteLine($"An IO error occured: {ex.Message}");
			Environment.Exit(1);
		}
		catch (Exception ex)
		{
			if (ex.InnerException is IOException)
				Console.Error.WriteLine($"An IO error occured: {ex.InnerException.Message}: {ex.Message}");
			else
				Console.Error.WriteLine($"An unhandled exception occured: {ex}");
			Environment.Exit(1);
		}
	}

	class SimpleLogger : ILogger
	{
		public void Log(string message)
		{
			Console.WriteLine(message);
		}

		public void LogWarning(string message)
		{
			Console.WriteLine($"Warning: {message}");
		}

		public void LogError(string message)
		{
			Console.Error.WriteLine($"Error: {message}");
		}
	}
}
