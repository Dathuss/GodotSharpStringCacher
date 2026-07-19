using System.Collections.Generic;
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

	[Output]
	public string OutputPdbFile { get; set; }

	[Output]
	public ITaskItem[] EmittedFiles { get; set; }

	public override bool Execute()
	{
		string intermediateDir = Common.GetAndCreateCacheDir(IntermediateOutputPath);
		Logger log = new(this);
		Config defaultConfig = new(UseLongNamesByDefault, WarnOnNonConstantImplicitOperator, log);

		string godotSharp = Common.GetGodotSharpFromReferencePath(ReferencePath, log);
		if (string.IsNullOrEmpty(godotSharp))
			return false;
		
		List<ITaskItem> emittedFiles = [];

		string newHash = Common.ComputeHash(IntermediateAssembly.ItemSpec, defaultConfig);

		string outputFile = Path.Combine(intermediateDir, Path.GetFileName(IntermediateAssembly.ItemSpec));
		string hashFile = outputFile + ".hash.cache";
		string warningsFile = outputFile + ".warnings.cache";
		string pdbFile = Path.ChangeExtension(outputFile, ".pdb");

		CachedIntermediateAssembly = IntermediateAssembly.CloneWithNewItemSpec(outputFile);
		emittedFiles.Add(CachedIntermediateAssembly);
		emittedFiles.Add(new TaskItem(hashFile));

		if (File.Exists(outputFile) && File.Exists(hashFile) && File.ReadAllText(hashFile) == newHash)
		{
			log.LogMessage($"Main assembly up to date");

			if (File.Exists(warningsFile))
			{
				Common.OutputCachedWarnings(warningsFile, log);
				emittedFiles.Add(new TaskItem(warningsFile));
			}

			if (File.Exists(pdbFile))
			{
				emittedFiles.Add(new TaskItem(pdbFile));
			}

			EmittedFiles = emittedFiles.ToArray();

			return true;
		}

		using Context ctx = new(defaultConfig);

		ctx.OpenGodotSharp(godotSharp);
		if (!Common.DoCache(ctx, IntermediateAssembly.ItemSpec, outputFile, AssemblyName, log))
		{
			return false;
		}

		// Depending on the build configuration, the output pdb file may not exist.
		if (File.Exists(pdbFile))
		{
			OutputPdbFile = pdbFile;
			emittedFiles.Add(new TaskItem(OutputPdbFile));
		}

		File.WriteAllText(hashFile, newHash);
		if (Common.CacheLoggerWarnings(warningsFile, log))
		{
			emittedFiles.Add(new TaskItem(warningsFile));
		}

		EmittedFiles = emittedFiles.ToArray();

		return true;
	}
}
