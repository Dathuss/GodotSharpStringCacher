using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace GodotSharpStringCacher.MSBuild;

public class GDStringMainAssemblyCacheTask : Task
{
	[Required]
	public string AssemblyName { get; set; }

	[Required]
	public ITaskItem IntermediateAssembly { get; set; }

	[Required]
	public ITaskItem[] ReferencePath { get; set; }

	[Required]
	public string IntermediateOutputPath { get; set; }


	[Required]
	public bool WarnOnNonConstantImplicitOperator { get; set; }

	[Required]
	public bool UseLongNamesByDefault { get; set; }


	[Output]
	public ITaskItem CachedIntermediateAssembly { get; set; }

	public override bool Execute()
	{
		string intermediateDir = Common.GetAndCreateCacheDir(IntermediateOutputPath);
		Logger log = new(this);
		Config defaultConfig = new(UseLongNamesByDefault, WarnOnNonConstantImplicitOperator, log);

		string godotSharp = Common.GetGodotSharpFromReferencePath(ReferencePath, log);
		if (string.IsNullOrEmpty(godotSharp))
			return false;

		string newHash = Common.ComputeHash(IntermediateAssembly.ItemSpec, defaultConfig);

		string outputFile = Path.Combine(intermediateDir, Path.GetFileName(IntermediateAssembly.ItemSpec));
		string hashFile = outputFile + ".hash.cache";
		string warningsFile = outputFile + ".warnings.cache";

		TaskItem cachedIntermediateAssemblyTaskItem = new(outputFile);
		IntermediateAssembly.CopyMetadataTo(cachedIntermediateAssemblyTaskItem);
		CachedIntermediateAssembly = cachedIntermediateAssemblyTaskItem;

		if (File.Exists(hashFile) && File.ReadAllText(hashFile) == newHash)
		{
			log.LogMessage($"Main assembly up to date");

			if (File.Exists(warningsFile))
			{
				Common.OutputCachedWarnings(warningsFile, log);
			}

			return true;
		}

		using Context ctx = new(defaultConfig);

		ctx.OpenGodotSharp(godotSharp);
		if (!Common.DoCache(ctx, IntermediateAssembly.ItemSpec, outputFile, AssemblyName, log))
		{
			return false;
		}

		File.WriteAllText(hashFile, newHash);
		Common.CacheLoggerWarnings(warningsFile, log);

		return true;
	}
}
