using System.Collections.Generic;
using System.Runtime.Serialization;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace GodotSharpStringCacher.MSBuild;

/// <summary>
/// A logger wrapping <see cref="TaskLoggingHelper"/>, exposing it to the backend as well.
/// </summary>
/// <param name="task">The target MSBuild task</param>
public class Logger(Task task) : LoggerBase
{
	public IReadOnlyCollection<SerializedWarningLog> Warnings => _warnings;

	readonly List<SerializedWarningLog> _warnings = [];

	public override void LogMessage(string message)
	{
		task.Log.LogMessage(message);
	}

	public override void LogMessage(string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message)
	{
		task.Log.LogMessage(null, null, null, file, lineNumber, columnNumber, endLineNumber, endColumnNumber, MessageImportance.Normal, message);
	}

	public override void LogWarning(string message)
	{
		_warnings.Add(new SerializedWarningLog(message, null, 0, 0, 0, 0));

		task.Log.LogWarning(message);
	}

	public override void LogWarning(string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message)
	{
		_warnings.Add(new SerializedWarningLog(message, file, lineNumber, columnNumber, endLineNumber, endColumnNumber));

		task.Log.LogWarning(null, null, null, file, lineNumber, columnNumber, endLineNumber, endColumnNumber, message);
	}

	public override void LogError(string message)
	{
		task.Log.LogError(message);
	}

	public override void LogError(string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message)
	{
		task.Log.LogError(null, null, null, file, lineNumber, columnNumber, endLineNumber, endColumnNumber, message);
	}

	[DataContract]
	public readonly record struct SerializedWarningLog(
		[property: DataMember] string Message,
		[property: DataMember] string? File,
		[property: DataMember] int Line,
		[property: DataMember] int Column,
		[property: DataMember] int EndLine,
		[property: DataMember] int EndColumn
	);
}
