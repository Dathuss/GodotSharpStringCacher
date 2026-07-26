using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace GodotSharpStringCacher.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NonConstStringOperatorAnalyzer : DiagnosticAnalyzer
{
	internal static readonly DiagnosticDescriptor _rule = new(
		id: "GDS001",
		title: "Implicit StringName or NodePath operator with non-constant string",
		messageFormat: "Implicit {0} operator with non-constant string",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "When making a StringName or NodePath object with a non-constant string argument, prefer using 'new StringName' or 'new NodePath'."
	);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(_rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterOperationAction(AnalyzeOperation, OperationKind.Conversion);
	}

	void AnalyzeOperation(OperationAnalysisContext context)
	{
		if (context.Operation is IConversionOperation
			{
				Operand.ConstantValue.HasValue: false,
				Operand.Type.SpecialType: SpecialType.System_String,
				Conversion.IsImplicit: true,
				Type:
				{
					ContainingAssembly.Name: "GodotSharp",
					Name: string conversionTypeName and ("StringName" or "NodePath"),
				}
			})
		{
			context.ReportDiagnostic(Diagnostic.Create(_rule, context.Operation.Syntax.GetLocation(), conversionTypeName));
		}			
	}
}
