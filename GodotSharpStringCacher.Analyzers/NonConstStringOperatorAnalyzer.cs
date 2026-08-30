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
					Name: string targetTypeName and ("StringName" or "NodePath"),
				}
			} conversion)
		{
			AnalyzeNestedOperation(context, targetTypeName, conversion.Operand);
		}
	}
	void AnalyzeNestedOperation(OperationAnalysisContext context, string targetTypeName, IOperation operation)
	{
		// Check branches of a ternary operator
		if (operation is IConditionalOperation conditionalValue && conditionalValue.WhenFalse is not null)
		{
			if (conditionalValue.WhenTrue.ConstantValue.HasValue && conditionalValue.WhenFalse.ConstantValue.HasValue)
			{
				return;
			}
			else if (conditionalValue.WhenTrue.ConstantValue.HasValue)
			{
				AnalyzeNestedOperation(context, targetTypeName, conditionalValue.WhenFalse);
				return;
			}
			else if (conditionalValue.WhenFalse.ConstantValue.HasValue)
			{
				AnalyzeNestedOperation(context, targetTypeName, conditionalValue.WhenTrue);
				return;
			}
		}

		context.ReportDiagnostic(Diagnostic.Create(
			Common.StringTypeImplicitOperatorWithNonConstantStringRule,
			operation.Syntax.GetLocation(),
			ImmutableDictionary.CreateRange<string, string?>([new("typeName", targetTypeName)]),
			targetTypeName));
	}
}
