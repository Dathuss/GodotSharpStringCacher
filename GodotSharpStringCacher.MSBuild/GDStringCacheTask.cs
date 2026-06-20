using System;
using System.Collections.Generic;
using System.Globalization;
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
	public string IntermediateOutputPath { get; set; }


	[Required]
	public bool CacheMainAssemblyStrings { get; set; }

	[Required]
	public bool WarnOnNonConstantImplicitOperator { get; set; }

	[Required]
	public bool UseLongNamesByDefault { get; set; }

	public override bool Execute()
	{
		if (CacheMainAssemblyStrings)
		{
			Config defaultConfig = new(UseLongNamesByDefault, WarnOnNonConstantImplicitOperator, new Common.SimpleLogger(this));
			using Context ctx = new(defaultConfig);

			string godotSharp = Common.GetGodotSharpFromReferencePath(ReferencePath, Log);
			if (string.IsNullOrEmpty(godotSharp))
				return false;

			ctx.OpenGodotSharp(godotSharp);
			string inputFile = $"{IntermediateOutputPath}{AssemblyName}.dll";
			string outputFile = $"{OutputPath}{AssemblyName}.dll";
			if (!Common.DoCache(ctx, inputFile, outputFile, AssemblyName, Log))
			{
				return false;
			}
		}

		return true;
	}
}
