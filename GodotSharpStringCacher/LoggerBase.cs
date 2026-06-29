public abstract class LoggerBase
{
	public abstract void LogMessage(string message);

	public virtual void LogMessage(string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message)
	{
		LogMessage($"{file}:{lineNumber}:{columnNumber}: {message}");
	}

	public abstract void LogWarning(string message);

	public virtual void LogWarning(string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message)
	{
		LogWarning($"{file}:{lineNumber}:{columnNumber}: {message}");
	}

	public abstract void LogError(string message);

	public virtual void LogError(string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message)
	{
		LogError($"{file}:{lineNumber}:{columnNumber}: {message}");
	}
}
