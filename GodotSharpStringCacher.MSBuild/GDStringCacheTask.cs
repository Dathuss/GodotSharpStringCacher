using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace GodotSharpStringCacher.MSBuild;

public class GDStringCacheTask : Task
{
	[Required]
	public string AssemblyName { get; set; }

	[Required]
	public string OutputPath { get; set; }

	[Required]
	public ITaskItem[] ReferencePath { get; set; }

	[Required]
	public ITaskItem[] PackageReference { get; set; }


	[Required]
	public bool CacheMainAssemblyStrings { get; set; }

	[Required]
	public ITaskItem[] CacheStrings { get; set; }

	[Required]
	public bool WarnOnNonConstantImplicitOperator { get; set; }

	[Required]
	public bool UseLongNamesByDefault { get; set; }

	bool CacheOne(Context ctx, string inputPath, string outputPath, string assemblyName)
	{
		Log.LogMessage($"{assemblyName}: Caching Godot strings...");
		try
		{
			ctx.RunAndSave(inputPath, outputPath);
			Log.LogMessage($"{assemblyName}: StringNames cached: {ctx.NumberOfStringNamesWritten}");
			Log.LogMessage($"{assemblyName}: NodePaths cached: {ctx.NumberOfNodePathsWritten}");
		}
		catch (NoGodotSharpReferenceExeption ex)
		{
			Log.LogWarning($"{assemblyName}: {ex}");
		}
		catch (IOException ex)
		{
			Log.LogError($"{assemblyName}: An IO error occured: {ex}");
			return false;
		}
		catch (Exception ex)
		{
			if (ex.InnerException is IOException)
				Log.LogError($"{assemblyName}: An IO error occured: {ex}");
			else
				Log.LogError($"{assemblyName}: An unhandled exception occured: {ex}");
			return false;
		}
		return true;
	}

	public override bool Execute()
	{
		SimpleLogger logger = new(this);
		Config defaultConfig = new(UseLongNamesByDefault, WarnOnNonConstantImplicitOperator, logger);
		using Context ctx = new(defaultConfig);

		Dictionary<string, ITaskItem> packagesToPatch = PackageReference.Where(x => GetBoolMetadata(x, "CacheStrings")).ToDictionary(x => x.ItemSpec);
		Dictionary<string, ITaskItem> assemblyNamesToPatch = CacheStrings.ToDictionary(x => x.ItemSpec);

		bool hasFoundGodotSharp = false;

		foreach (ITaskItem reference in ReferencePath)
		{
			string fileName = reference.GetMetadata("FileName");
			if (fileName == "GodotSharp")
			{
				string fullPath = reference.GetMetadata("FullPath");
				ctx.OpenGodotSharp(fullPath);
				hasFoundGodotSharp = true;
				break;
			}
		}

		if (!hasFoundGodotSharp)
		{
			Log.LogError("No GodotSharp reference found in the project. Make sure you reference it or that you use Godot.NET.Sdk.");
			return false;
		}

		foreach (ITaskItem reference in ReferencePath)
		{
			string fileName = reference.GetMetadata("FileName");
			if (fileName == "GodotSharp")
				continue;

			ITaskItem assemblyTask;

			// Checks for <ProjectReference> and <Reference>
			if (GetBoolMetadata(reference, "CacheStrings")) { assemblyTask = reference; }
			// Checks for <PackageReference>
			else if (TryGetMetadata(reference, "NuGetPackageId", out string nuGetPackageId) && packagesToPatch.TryGetValue(nuGetPackageId, out assemblyTask)) { }
			// Checks for <CacheStrings>
			else if (assemblyNamesToPatch.TryGetValue(fileName, out assemblyTask)) { }
			else continue;

			string fullPath = reference.GetMetadata("FullPath");

			ctx.Config = ParseConfig(assemblyTask, defaultConfig);
			string outputFile = $"{OutputPath}{fileName}{reference.GetMetadata("Extension")}";
			if (!CacheOne(ctx, fullPath, outputFile, fileName))
			{
				return false;
			}
		}
	
		if (CacheMainAssemblyStrings)
		{
			string path = $"{OutputPath}{AssemblyName}.dll";
			if (!CacheOne(ctx, path, path, AssemblyName))
				return false;
		}

		return true;
	}

	static Config ParseConfig(ITaskItem taskWithOptions, Config defaultConfig)
	{
		bool GetBool(string name, bool fallback)
		{
			return HasMetadata(taskWithOptions, name) ? GetBoolMetadata(taskWithOptions, name) : fallback;
		}

		return new Config(
			GetBool("LongNames", defaultConfig.UseLongNames),
			GetBool("WarnOnNonConstantImplicitOperator", defaultConfig.WarnOnNonConstantImplicitOperator),
			defaultConfig.Logger);
	}

	static bool HasMetadata(ITaskItem taskItem, string name) => ((ICollection<string>)taskItem.MetadataNames).Contains(name);

	static bool TryGetMetadata(ITaskItem taskItem, string name, out string value)
	{
		if (HasMetadata(taskItem, name))
		{
			value = taskItem.GetMetadata(name);
			return true;
		}
		value = null;
		return false;
	}

	static bool GetBoolMetadata(ITaskItem taskItem, string name)
	{
		return taskItem.GetMetadata(name).Equals("true", StringComparison.OrdinalIgnoreCase);
	}

	class SimpleLogger(Task task) : ILogger
	{
		public void Log(string message)
		{
			task.Log.LogMessage(message);
		}

		public void LogError(string message)
		{
			task.Log.LogError(message);
		}

		public void LogWarning(string message)
		{
			task.Log.LogWarning(message);
		}
	}
}
