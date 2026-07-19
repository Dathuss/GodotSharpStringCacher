using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace GodotSharpStringCacher.MSBuild;

public class GDStringMainAssemblyCacheTask : Task
{
	[Required]
	public string AssemblyName { get; set; } = null!;

	[Required]
	public ITaskItem IntermediateAssembly { get; set; } = null!;

	[Required]
	public ITaskItem[] ReferencePath { get; set; } = null!;

	[Required]
	public string IntermediateOutputPath { get; set; } = null!;


	[Required]
	public bool WarnOnNonConstantImplicitOperator { get; set; } = false;

	[Required]
	public bool UseLongNamesByDefault { get; set; } = false;


	[Output]
	public ITaskItem CachedIntermediateAssembly { get; set; } = null!;

	[Output]
	public string OutputPdbFile { get; set; } = null!;

	[Output]
	public ITaskItem[] EmittedFiles { get; set; } = null!;

	public override bool Execute()
	{
		string intermediateDir = Common.GetAndCreateCacheDir(IntermediateOutputPath);
		Logger log = new(this);
		Config defaultConfig = new(UseLongNamesByDefault, WarnOnNonConstantImplicitOperator, log);

		string? godotSharp = Common.GetGodotSharpFromReferencePath(ReferencePath, log);
		if (string.IsNullOrEmpty(godotSharp))
			return false;
		
		List<ITaskItem> emittedFiles = [];

		string newHash = Common.ComputeHash(IntermediateAssembly.ItemSpec, defaultConfig);

		string outputFile = Path.Combine(intermediateDir, Path.GetFileName(IntermediateAssembly.ItemSpec));
		string hashFile = outputFile + ".hash.cache";
		string warningsFile = outputFile + ".warnings.cache";
		string pdbFile = Context.GetPdbFileName(outputFile);

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

		ctx.OpenGodotSharp(godotSharp!);
		if (!Common.DoCache(ctx, IntermediateAssembly.ItemSpec, outputFile, AssemblyName, log, out bool isPdbFileOutputted))
		{
			return false;
		}

		// Depending on the build configuration, the output PDB file may not exist.
		if (isPdbFileOutputted)
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
