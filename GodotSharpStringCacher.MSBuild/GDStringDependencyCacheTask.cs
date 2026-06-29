using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace GodotSharpStringCacher.MSBuild;

/// <summary>
/// Patches references, caches them, and replaces their MSBuild reference pathes with the cached path
/// </summary>
public class GDStringDependencyCacheTask : Task
{
	[Required]
	public string IntermediateOutputPath { get; set; }

	[Required]
	public ITaskItem[] ReferencePath { get; set; }

	[Required]
	public ITaskItem[] ReferenceCopyLocalPaths { get; set; }

	[Required]
	public ITaskItem[] PackageReference { get; set; }


	[Required]
	public ITaskItem[] CacheStrings { get; set; }

	[Required]
	public bool WarnOnNonConstantImplicitOperator { get; set; }

	[Required]
	public bool UseLongNamesByDefault { get; set; }

	
	[Output]
	public ITaskItem[] RemovedReferencePath { get; set; }

	[Output]
	public ITaskItem[] AddedReferencePath { get; set; }

	[Output]
	public ITaskItem[] RemovedReferenceCopyLocalPaths { get; set; }

	[Output]
	public ITaskItem[] AddedReferenceCopyLocalPaths { get; set; }

	public override bool Execute()
	{
		string intermediateDir = Common.GetAndCreateCacheDir(IntermediateOutputPath);

		Dictionary<string, ITaskItem> packagesToPatch = PackageReference.Where(x => x.GetBoolMetadata("CacheStrings")).ToDictionary(x => x.ItemSpec);
		Dictionary<string, ITaskItem> assemblyNamesToPatch = CacheStrings.ToDictionary(x => x.ItemSpec);

		List<ITaskItem> removedReferencePath = [];
		List<ITaskItem> addedReferencePath = [];

		List<ITaskItem> removedReferenceCopyLocalPaths = [];
		List<ITaskItem> addedReferenceCopyLocalPaths = [];

		Context ctx = null;

		try
		{
			foreach (ITaskItem reference in ReferencePath)
			{
				string fileName = reference.GetMetadata("FileName");
				if (fileName == "GodotSharp")
					continue;

				ITaskItem assemblyTaskItem;

				// Checks for <ProjectReference> and <Reference>
				if (reference.GetBoolMetadata("CacheStrings")) { assemblyTaskItem = reference; }
				// Checks for <PackageReference>
				else if (reference.TryGetMetadata("NuGetPackageId", out string nuGetPackageId) && packagesToPatch.TryGetValue(nuGetPackageId, out assemblyTaskItem)) { }
				// Checks for <CacheStrings>
				else if (assemblyNamesToPatch.TryGetValue(fileName, out assemblyTaskItem)) { }
				else continue;

				Common.SimpleLogger backendLogger = new(this);
				Config defaultConfig = new(UseLongNamesByDefault, WarnOnNonConstantImplicitOperator, backendLogger);
				if (ctx == null)
				{
					string godotSharp = Common.GetGodotSharpFromReferencePath(ReferencePath, Log);
					if (string.IsNullOrEmpty(godotSharp))
						return false;

					ctx = new Context(defaultConfig);
					ctx.OpenGodotSharp(godotSharp);
				}

				ctx.Config = ParseConfig(assemblyTaskItem, defaultConfig);

				string fullPath = reference.GetMetadata("FullPath");
				string newHash = Common.ComputeHash(fullPath, ctx.Config);

				string outputFile = Path.Combine(intermediateDir, Path.GetFileName(fullPath));
				string hashFile = outputFile + ".hash.cache";
				string warningsFile = outputFile + ".warnings.cache";

				// Replace ReferencePath and ReferenceCopyLocalPaths to the cached path
				removedReferencePath.Add(reference);

				TaskItem cachedReference = new(outputFile);
				reference.CopyMetadataTo(cachedReference);
				addedReferencePath.Add(cachedReference);

				ITaskItem referenceOfReferenceCopyLocalPaths = ReferenceCopyLocalPaths.First(x => x.GetMetadata("FileName") == fileName && x.GetMetadata("Extension") == ".dll");
				removedReferenceCopyLocalPaths.Add(referenceOfReferenceCopyLocalPaths);

				TaskItem cachedReferenceForCopy = new(outputFile);
				referenceOfReferenceCopyLocalPaths.CopyMetadataTo(cachedReferenceForCopy);
				addedReferenceCopyLocalPaths.Add(cachedReferenceForCopy);

				if (File.Exists(hashFile) && File.ReadAllText(hashFile) == newHash)
				{
					Log.LogMessage($"Assembly {fileName} up to date");

					// Output cached warnings
					if (File.Exists(warningsFile))
					{
						foreach (string warning in File.ReadLines(warningsFile).Where(warning => !string.IsNullOrEmpty(warning)))
						{
							Log.LogWarning(warning);
						}
					}

					continue;
				}

				if (!Common.DoCache(ctx, fullPath, outputFile, fileName, Log))
				{
					return false;
				}

				File.WriteAllText(hashFile, newHash);
				File.WriteAllLines(warningsFile, backendLogger.Warnings);
			}
		}
		finally
		{
			ctx?.Dispose();
		}

		RemovedReferencePath = removedReferencePath.ToArray();
		AddedReferencePath = addedReferencePath.ToArray();

		RemovedReferenceCopyLocalPaths = removedReferenceCopyLocalPaths.ToArray();
		AddedReferenceCopyLocalPaths = addedReferenceCopyLocalPaths.ToArray();

		return true;
	}

	static Config ParseConfig(ITaskItem taskWithOptions, Config defaultConfig)
	{
		bool GetBool(string name, bool fallback)
		{
			return taskWithOptions.HasMetadata(name) ? taskWithOptions.GetBoolMetadata(name) : fallback;
		}

		return new Config(
			GetBool("LongNames", defaultConfig.UseLongNames),
			GetBool("WarnOnNonConstantImplicitOperator", defaultConfig.WarnOnNonConstantImplicitOperator),
			defaultConfig.Logger);
	}
}
