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
	/// Returns true if the operation yields a patchable value (normally a constant string).
	/// <br/><br/>
	/// If the expression is a ternary expression or a switch expression, only the branches are considered, even if the condition is non-constant.
	/// This is to allow patching of ternary expressions and switch expressions where the branches are constant but typed as string.
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

		// Constant string expression or StringName/NodePath expression
		if ((operation.ConstantValue.HasValue && operation.ConstantValue.Value is string)
			|| SymbolEqualityComparer.Default.Equals(operation.Type, baselineType))
		{
			return true;
		}
		// Ternary expression
		else if (operation is IConditionalOperation conditionalOperation)
		{
			bool isTrueBranchConstant = AnalyzeNestedOperations(
				GetInnerOperation(conditionalOperation.WhenTrue),
				conditionalOperation.WhenTrue.Syntax.GetLocation(),
				outOperations,
				baselineType);

			bool isFalseBranchConstant = true;
			if (conditionalOperation.WhenFalse is not null)
			{
				isFalseBranchConstant = AnalyzeNestedOperations(
					GetInnerOperation(conditionalOperation.WhenFalse),
					conditionalOperation.WhenFalse.Syntax.GetLocation(),
					outOperations,
					baselineType);
			}

			return isTrueBranchConstant && isFalseBranchConstant;
		}
		// Switch expression
		else if (operation is ISwitchExpressionOperation switchExpression)
		{
			bool areAllSwitchArmsConstant = true;
			foreach (ISwitchExpressionArmOperation arm in switchExpression.Arms)
			{
				IOperation armInner = GetInnerOperation(arm.Value);

				if (armInner is IThrowOperation)
					continue;

				bool isSwitchArmConstant = AnalyzeNestedOperations(
					armInner,
					arm.Value.Syntax.GetLocation(),
					outOperations,
					baselineType);

				if (!isSwitchArmConstant)
					areAllSwitchArmsConstant = false;
			}
			return areAllSwitchArmsConstant;
		}
		// Non-constant expression
		else
		{
			outOperations?.Add(diagnosticLocation);
			return false;
		}
	}
}
