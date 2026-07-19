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
	public string IntermediateOutputPath { get; set; } = null!;

	[Required]
	public ITaskItem[] ReferencePath { get; set; } = null!;

	[Required]
	public ITaskItem[] ReferenceCopyLocalPaths { get; set; } = null!;

	[Required]
	public ITaskItem[] PackageReference { get; set; } = null!;


	[Required]
	public ITaskItem[] CacheStrings { get; set; } = null!;

	[Required]
	public bool WarnOnNonConstantImplicitOperator { get; set; } = false;

	[Required]
	public bool UseLongNamesByDefault { get; set; } = false;

	
	[Output]
	public ITaskItem[] RemovedReferencePath { get; set; } = null!;

	[Output]
	public ITaskItem[] AddedReferencePath { get; set; } = null!;

	[Output]
	public ITaskItem[] RemovedReferenceCopyLocalPaths { get; set; } = null!;

	[Output]
	public ITaskItem[] AddedReferenceCopyLocalPaths { get; set; } = null!;

	[Output]
	public ITaskItem[] EmittedFiles { get; set; } = null!;

	public override bool Execute()
	{
		string intermediateDir = Common.GetAndCreateCacheDir(IntermediateOutputPath);

		Dictionary<string, ITaskItem> packagesToPatch = PackageReference.Where(x => x.GetBoolMetadata("CacheStrings")).ToDictionary(x => x.ItemSpec);
		Dictionary<string, ITaskItem> assemblyNamesToPatch = CacheStrings.ToDictionary(x => x.ItemSpec);

		List<ITaskItem> removedReferencePath = [];
		List<ITaskItem> addedReferencePath = [];

		List<ITaskItem> removedReferenceCopyLocalPaths = [];
		List<ITaskItem> addedReferenceCopyLocalPaths = [];

		List<ITaskItem> emittedFiles = [];

		Context? ctx = null;

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
				else if (reference.TryGetMetadata("NuGetPackageId", out string? nuGetPackageId) && packagesToPatch.TryGetValue(nuGetPackageId!, out assemblyTaskItem)) { }
				// Checks for <CacheStrings>
				else if (assemblyNamesToPatch.TryGetValue(fileName, out assemblyTaskItem)) { }
				else continue;

				Logger log = new(this);
				Config defaultConfig = new(UseLongNamesByDefault, WarnOnNonConstantImplicitOperator, log);
				if (ctx == null)
				{
					string? godotSharp = Common.GetGodotSharpFromReferencePath(ReferencePath, log);
					if (string.IsNullOrEmpty(godotSharp))
						return false;

					ctx = new Context(defaultConfig);
					ctx.OpenGodotSharp(godotSharp!);
				}

				ctx.Config = ParseConfig(assemblyTaskItem, defaultConfig);

				string fullPath = reference.GetMetadata("FullPath");
				string newHash = Common.ComputeHash(fullPath, ctx.Config);

				string outputFile = Path.Combine(intermediateDir, Path.GetFileName(fullPath));
				string hashFile = outputFile + ".hash.cache";
				string warningsFile = outputFile + ".warnings.cache";
				string pdbFile = Context.GetPdbFileName(outputFile);

				// Replace ReferencePath and ReferenceCopyLocalPaths to the cached path
				removedReferencePath.Add(reference);

				TaskItem cachedReference = reference.CloneWithNewItemSpec(outputFile);
				addedReferencePath.Add(cachedReference);
				emittedFiles.Add(cachedReference);

				ITaskItem referenceOfReferenceCopyLocalPaths = ReferenceCopyLocalPaths.First(x => x.GetMetadata("FileName") == fileName && x.GetMetadata("Extension") == ".dll");
				removedReferenceCopyLocalPaths.Add(referenceOfReferenceCopyLocalPaths);

				TaskItem cachedReferenceForCopy = referenceOfReferenceCopyLocalPaths.CloneWithNewItemSpec(outputFile);
				addedReferenceCopyLocalPaths.Add(cachedReferenceForCopy);

				// Try to replace symbol file of ReferenceCopyLocalPaths
				ITaskItem pdbOfReferenceCopyLocalPaths = ReferenceCopyLocalPaths.FirstOrDefault(x => x.GetMetadata("FileName") == fileName && x.GetMetadata("Extension") == ".pdb");
				if (pdbOfReferenceCopyLocalPaths != null)
				{
					removedReferenceCopyLocalPaths.Add(pdbOfReferenceCopyLocalPaths);

					TaskItem cachedPdbForCopy = pdbOfReferenceCopyLocalPaths.CloneWithNewItemSpec(pdbFile);
					addedReferenceCopyLocalPaths.Add(cachedPdbForCopy);
					emittedFiles.Add(cachedPdbForCopy);
				}

				emittedFiles.Add(new TaskItem(hashFile));

				if (File.Exists(outputFile) && File.Exists(hashFile) && File.ReadAllText(hashFile) == newHash)
				{
					log.LogMessage($"Assembly {fileName} up to date");

					if (File.Exists(warningsFile))
					{
						Common.OutputCachedWarnings(warningsFile, log);
						emittedFiles.Add(new TaskItem(warningsFile));
					}

					if (File.Exists(pdbFile))
					{
						emittedFiles.Add(new TaskItem(pdbFile));
					}

					continue;
				}

				if (!Common.DoCache(ctx, fullPath, outputFile, fileName, log, out bool isPdbFileOutputted))
				{
					return false;
				}

				if (pdbOfReferenceCopyLocalPaths != null && !isPdbFileOutputted)
				{
					// Unlikely to happen, but it's better to record it
					Log.LogWarning($"Dependency {fileName} was supposed to output a PDB file when patched, but it didn't. This shouldn't happen.");
					emittedFiles.RemoveAll(x => x.ItemSpec == pdbFile);
				}

				File.WriteAllText(hashFile, newHash);
				if (Common.CacheLoggerWarnings(warningsFile, log))
				{
					emittedFiles.Add(new TaskItem(warningsFile));
				}
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

		EmittedFiles = emittedFiles.ToArray();

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
