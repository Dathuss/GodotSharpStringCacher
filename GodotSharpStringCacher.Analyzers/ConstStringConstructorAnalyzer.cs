using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace GodotSharpStringCacher.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConstStringConstructorAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Common.StringTypeConstructorWithConstantStringRule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeObjectCreationSyntax, SyntaxKind.ObjectCreationExpression);
		context.RegisterSyntaxNodeAction(AnalyzeObjectCreationSyntax, SyntaxKind.ImplicitObjectCreationExpression);
	}

	void AnalyzeObjectCreationSyntax(SyntaxNodeAnalysisContext context)
	{
		SemanticModel semanticModel = context.SemanticModel;
		SyntaxNode node = context.Node;

		if (semanticModel.GetOperation(node, context.CancellationToken) is IObjectCreationOperation
			{
				Type:
				{
					ContainingAssembly.Name: "GodotSharp",
					Name: string ctorTypeName and ("StringName" or "NodePath")
				},
				Arguments: ImmutableArray<IArgumentOperation> args
			} && args.Length == 1 && CheckArgumentIsConstant(args[0].Value))
		{
			context.ReportDiagnostic(Diagnostic.Create(
				Common.StringTypeConstructorWithConstantStringRule,
				node.GetLocation(),
				ImmutableDictionary.CreateRange<string, string?>([new("typeName", ctorTypeName)]),
				ctorTypeName));
		}
	}

	static bool CheckArgumentIsConstant(IOperation operation)
	{
		if (operation.ConstantValue.Value is string)
			return true;
		// Check if both paths of a ternary operator are constant
		if (operation is IConditionalOperation { WhenFalse: not null } conditionalOperation)
		{
			return conditionalOperation.WhenTrue.ConstantValue.Value is string
				&& conditionalOperation.WhenFalse.ConstantValue.Value is string;
		}
		return false;
	}
}
