using System.Collections.Generic;
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
			} conversionOperation)
		{
			Location diagnosticLocation = context.Operation.Syntax.GetLocation();
			List<Location> outLocations = [];
			AnalyzeNestedOperations(conversionOperation.Operand, diagnosticLocation, outLocations, conversionOperation.Type!);

			foreach (Location location in outLocations)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					Common.StringTypeImplicitOperatorWithNonConstantStringRule,
					location,
					ImmutableDictionary.CreateRange<string, string?>([new("typeName", conversionTypeName)]),
					conversionTypeName));
			}
		}
	}

	/// <summary>
	/// Returns true if the operation yields a constant string value.
	/// Takes into account switch expressions and ternaries.
	/// </summary>
	/// <param name="diagnosticLocation">Location of the operation, including the conversion operation that may contain it.</param>
	/// <param name="outOperations">If non-null, gets filled with the locations of the non-constant operations inside the operation.</param>
	/// <param name="baselineType">The type to compare against (StringName/NodePath) when testing if value is non-constant.</param>
	/// <returns></returns>
	public static bool AnalyzeNestedOperations(IOperation operation, Location diagnosticLocation, IList<Location>? outOperations, ISymbol baselineType)
	{
		static IOperation GetInnerOperation(IOperation op)
		{
			return op is IConversionOperation conversion ? conversion.Operand : op;
		}

		// Check branches of a ternary operator
		if (operation is IConditionalOperation { WhenFalse: not null } conditionalOperation)
		{
			return AnalyzeNestedOperations(GetInnerOperation(conditionalOperation.WhenTrue), conditionalOperation.WhenTrue.Syntax.GetLocation(), outOperations, baselineType)
				& AnalyzeNestedOperations(GetInnerOperation(conditionalOperation.WhenFalse), conditionalOperation.WhenFalse.Syntax.GetLocation(), outOperations, baselineType);
		}
		else if (operation is ISwitchExpressionOperation switchExpression)
		{
			bool isConstant = true;
			foreach (ISwitchExpressionArmOperation arm in switchExpression.Arms)
			{
                // It's possible that a conversion operation wraps another switch operation or even a throw operation.
                // We'll unwrap it to continue finding all the outcomes
                IOperation innerOp = GetInnerOperation(arm.Value);
				if (innerOp is not IThrowOperation)
				{
					isConstant &= !AnalyzeNestedOperations(innerOp, arm.Value.Syntax.GetLocation(), outOperations, baselineType);
				}
			}
			return isConstant;
		}
		else if (operation.ConstantValue.Value is not string && !SymbolEqualityComparer.Default.Equals(operation.Type, baselineType))
		{
			outOperations?.Add(diagnosticLocation);
			return false;
		}
		return true;
	}
}
