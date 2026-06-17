
/// <summary>
/// Very simple logger interface
/// </summary>
public interface ILogger
{
	void Log(string message);

	void LogWarning(string message);
	
	void LogError(string message);
}
