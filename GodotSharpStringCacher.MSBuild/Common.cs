using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace GodotSharpStringCacher.MSBuild;

internal static class Common
{
	public static string GetGodotSharpFromReferencePath(ITaskItem[] referencePath, Logger log)
	{
		foreach (ITaskItem reference in referencePath)
		{
			string fileName = reference.GetMetadata("FileName");
			if (fileName == "GodotSharp")
			{
				string fullPath = reference.GetMetadata("FullPath");
				return fullPath;
			}
		}

		log.LogError("No GodotSharp reference found in the project. Make sure you reference it or that you use Godot.NET.Sdk.");
		return null;
	}

	public static bool DoCache(Context ctx, string inputPath, string outputPath, string assemblyName, Logger log)
	{
		log.LogMessage($"{assemblyName}: Caching Godot strings...");
		try
		{
			ctx.RunAndSave(inputPath, outputPath);
			log.LogMessage($"{assemblyName}: StringNames cached: {ctx.NumberOfStringNamesWritten}");
			log.LogMessage($"{assemblyName}: NodePaths cached: {ctx.NumberOfNodePathsWritten}");
		}
		catch (NoGodotSharpReferenceExeption ex)
		{
			log.LogWarning($"{assemblyName}: {ex}");
		}
		catch (IOException ex)
		{
			log.LogError($"{assemblyName}: An IO error occured: {ex}");
			return false;
		}
		catch (Exception ex)
		{
			if (ex.InnerException is IOException)
				log.LogError($"{assemblyName}: An IO error occured: {ex}");
			else
				log.LogError($"{assemblyName}: An unhandled exception occured: {ex}");
			return false;
		}
		return true;
	}

	public static string GetAndCreateCacheDir(string intermediateOutputPath)
	{
		string intermediateDir = Path.Combine(intermediateOutputPath, "string-cache");
		Directory.CreateDirectory(intermediateDir);
		return intermediateDir;
	}

	/// <summary>
	/// Computes a unique hash that takes into account the input file timestamp, the caching config,
	/// and the current cacher version
	/// </summary>
	public static string ComputeHash(string inputFile, Config config)
	{
		using SHA256 hash = SHA256.Create();

		void Hash(byte[] buffer, bool isFinalBlock = false)
		{
			if (isFinalBlock)
			{
				hash.TransformFinalBlock(buffer, 0, buffer.Length);
			}
			else
			{
				hash.TransformBlock(buffer, 0, buffer.Length, buffer, 0);
			}
		}
		void HashString(string str, bool isFinalBlock = false) => Hash(Encoding.UTF8.GetBytes(str), isFinalBlock);
		void HashBool(bool value, bool isFinalBlock = false) => Hash(BitConverter.GetBytes(value), isFinalBlock);
		void HashLong(long value, bool isFinalBlock = false) => Hash(BitConverter.GetBytes(value), isFinalBlock);

		HashString(typeof(GDStringDependencyCacheTask).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);
		HashString(typeof(Context).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);

		HashBool(config.UseLongNames);
		HashBool(config.WarnOnNonConstantImplicitOperator);

		HashLong(File.GetLastWriteTimeUtc(inputFile).ToBinary(),
			isFinalBlock: true);

		return string.Concat(hash.Hash.Select(x => x.ToString("x2", CultureInfo.InvariantCulture)));
	}

	public static bool HasMetadata(this ITaskItem taskItem, string name) => ((ICollection<string>)taskItem.MetadataNames).Contains(name);

	public static bool TryGetMetadata(this ITaskItem taskItem, string name, out string value)
	{
		if (HasMetadata(taskItem, name))
		{
			value = taskItem.GetMetadata(name);
			return true;
		}
		value = null;
		return false;
	}

	public static bool GetBoolMetadata(this ITaskItem taskItem, string name)
	{
		return taskItem.GetMetadata(name).Equals("true", StringComparison.OrdinalIgnoreCase);
	}

	public class Logger(Task task) : LoggerBase
	{
		public IReadOnlyCollection<string> Warnings => _warnings;

		readonly List<string> _warnings = [];

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
			_warnings.Add(message);
			task.Log.LogWarning(message);
		}

		public override void LogWarning(string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message)
		{
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
	}
}
