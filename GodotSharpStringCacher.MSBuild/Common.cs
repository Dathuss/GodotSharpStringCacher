using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;

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

	public static void CacheLoggerWarnings(string warningsFile, Logger log)
	{
		try
		{
			if (log.Warnings.Count == 0)
			{
				// Removes the file if it was there previously (otherwise older warnings will appear)
				File.Delete(warningsFile);
				return;
			}
			using FileStream fs = File.Create(warningsFile);
			JsonHelper.Serialize(log.Warnings.ToArray(), fs);
		}
		catch
		{
			log.LogWarning("Failed to update warnings file");
		}
	}

	public static void OutputCachedWarnings(string warningsFile, LoggerBase log)
	{
		try
		{
			using FileStream fs = File.OpenRead(warningsFile);
			foreach (Logger.SerializedWarningLog warningLog in JsonHelper.Deserialize<Logger.SerializedWarningLog[]>(fs))
			{
				if (warningLog.File != null)
				{
					log.LogWarning(warningLog.File, warningLog.Line, warningLog.Column, warningLog.EndLine, warningLog.EndColumn, warningLog.Message);
				}
				else
				{
					log.LogWarning(warningLog.Message);
				}
			}
		}
		catch
		{
			log.LogWarning("Failed to deserialize warnings file");
		}
	}
}
