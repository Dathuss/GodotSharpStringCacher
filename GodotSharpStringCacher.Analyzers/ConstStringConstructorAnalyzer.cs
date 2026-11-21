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

		IOperation? op = semanticModel.GetOperation(node, context.CancellationToken);

		if (op is IObjectCreationOperation
			{
				Type:
				{
					ContainingAssembly.Name: "GodotSharp",
					Name: string ctorTypeName and ("StringName" or "NodePath")
				},
				Arguments: [IArgumentOperation stringArg]
			} && NonConstStringOperatorAnalyzer.AnalyzeNestedOperations(stringArg.Value, Location.None, null, op.Type))
		{
			context.ReportDiagnostic(Diagnostic.Create(
				Common.StringTypeConstructorWithConstantStringRule,
				node.GetLocation(),
				ImmutableDictionary.CreateRange<string, string?>([new("typeName", ctorTypeName)]),
				ctorTypeName));
		}
	}
}
