using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace GodotSharpStringCacher.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NonConstStringOperatorAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Common.StringTypeImplicitOperatorWithNonConstantStringRule);

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
				Type:
				{
					ContainingAssembly.Name: "GodotSharp",
					Name: string conversionTypeName and ("StringName" or "NodePath"),
				}
			})
		{
			context.ReportDiagnostic(Diagnostic.Create(
				Common.StringTypeImplicitOperatorWithNonConstantStringRule,
				context.Operation.Syntax.GetLocation(),
				ImmutableDictionary.CreateRange<string, string?>([new("typeName", conversionTypeName)]),
				conversionTypeName));
		}
	}
}
