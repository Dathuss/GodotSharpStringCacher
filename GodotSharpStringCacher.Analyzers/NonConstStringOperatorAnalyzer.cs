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

	static bool AnalyzeNestedOperations(IOperation operation, Location diagnosticLocation, List<Location> outOperations, ISymbol baselineType)
	{
		// Check branches of a ternary operator
		if (operation is IConditionalOperation { WhenFalse: not null } conditionalOperation)
		{
			if (conditionalOperation.WhenTrue.ConstantValue.HasValue && conditionalOperation.WhenFalse.ConstantValue.HasValue)
			{
				return false;
			}
			else if (conditionalOperation.WhenTrue.ConstantValue.HasValue)
			{
				diagnosticLocation = conditionalOperation.WhenFalse.Syntax.GetLocation();
			}
			else if (conditionalOperation.WhenFalse.ConstantValue.HasValue)
			{
				diagnosticLocation = conditionalOperation.WhenTrue.Syntax.GetLocation();
			}
			// When both paths are not constant, warn for the whole ternary

			// TODO: implement chained ternaries
			// They are a pain because even the compiler doesn't handle them well, it
			// will insert useless implicit conversions, which have the effect of
			// generating unoptimized CIL as well as being hard to handle programmatically.
			outOperations.Add(diagnosticLocation);
		}
		else if (operation is ISwitchExpressionOperation switchExpression)
		{
			bool isConstant = true;
			foreach (ISwitchExpressionArmOperation arm in switchExpression.Arms)
			{
                // It's possible that a conversion operation wraps another switch operation or even a throw operation.
                // We'll unwrap it to continue finding all the outcomes
                IOperation innerOp = arm.Value is IConversionOperation conversion ? conversion.Operand : arm.Value;
				if (innerOp is not IThrowOperation)
				{
					isConstant &= !AnalyzeNestedOperations(innerOp, arm.Value.Syntax.GetLocation(), outOperations, baselineType);
				}
			}
			return isConstant;
		}
		else if (operation.ConstantValue.Value is not string && !SymbolEqualityComparer.Default.Equals(operation.Type, baselineType))
		{
			outOperations.Add(diagnosticLocation);
			return false;
		}
		return true;
	}
}
