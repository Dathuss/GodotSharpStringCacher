using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace GodotSharpStringCacher.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NonConstStringOperatorCodeFixProvider))]
public sealed class NonConstStringOperatorCodeFixProvider : CodeFixProvider
{
	public override ImmutableArray<string> FixableDiagnosticIds
		=> ImmutableArray.Create(Common.StringTypeImplicitOperatorWithNonConstantStringRule.Id);

	public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		SemanticModel? semanticModel = await context.Document.GetSemanticModelAsync().ConfigureAwait(false);
		if (root == null || semanticModel == null)
			return;

		Diagnostic diagnostic = context.Diagnostics.First();

		TextSpan diagnosticSpan = diagnostic.Location.SourceSpan;

		SyntaxNode syntaxNode = root.FindNode(diagnosticSpan);

		CodeAction? codeAction = GetFixForNonConstImplicitStringOperator(context, syntaxNode, diagnostic, semanticModel);

		if (codeAction != null)
		{
			context.RegisterCodeFix(
				codeAction,
				context.Diagnostics
			);
		}
	}

	static CodeAction? GetFixForNonConstImplicitStringOperator(CodeFixContext context,
		SyntaxNode syntaxNode, Diagnostic diagnostic, SemanticModel semanticModel)
	{
		ExpressionSyntax expression;

		if (syntaxNode is ExpressionSyntax syntax)
		{
			// Example: "NodePath nodePath = NetworkPacket.StringValue;"
			// Here, "NetworkPacket.StringValue" is selected.
			expression = syntax;
		}
		else if (syntaxNode is ArgumentSyntax argumentSyntax)
		{
			// Example: "return GetNodeOrNull(NetworkPacket.StringValue);"
			// Here, "NetworkPacket.StringValue" is selected.
			expression = argumentSyntax.Expression;
		}
		else
		{
			return null;
		}

		// Guaranteed to be either "StringName" or "NodePath"
		string typeName = diagnostic.Properties["typeName"]!;

		return CodeAction.Create(
			title: $"Add explicit {typeName} constructor",
			createChangedDocument: ct => AddExplicitConstructorAsync(context.Document, semanticModel, typeName, expression, ct),
			equivalenceKey: $"{typeName}_addCtor"
		);
	}

	static async Task<Document> AddExplicitConstructorAsync(Document document, SemanticModel semanticModel,
		string typeName, ExpressionSyntax expressionToBuild, CancellationToken ct)
	{
		// Remove explicit cast to StringName/NodePath if present
		ExpressionSyntax expressionInsideConstructor = expressionToBuild;
		if (expressionToBuild is CastExpressionSyntax castExpression)
		{
			if (semanticModel.GetSymbolInfo(castExpression.Type, ct).Symbol?.Name == typeName)
			{
				expressionInsideConstructor = castExpression.Expression;
			}
		}

		// For any expression "expr", replace it with "new StringName(expr)"/"new NodePath(expr)"
		ObjectCreationExpressionSyntax objectCreationExpression = SyntaxFactory.ObjectCreationExpression(
			type: SyntaxFactory.IdentifierName(typeName),
			argumentList: SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
				SyntaxFactory.Argument(expressionInsideConstructor)
			)),
			initializer: null
		);

		SyntaxNode oldRoot = (await document.GetSyntaxRootAsync(ct).ConfigureAwait(false))!;
		SyntaxNode newRoot = oldRoot.ReplaceNode(expressionToBuild, objectCreationExpression);
		if (newRoot is CompilationUnitSyntax compilationUnit)
		{
			// Check if the symbol "StringName"/"NodePath" is accessible
			ISymbol? stringConversionTypeSymbol = semanticModel.GetSpeculativeSymbolInfo(
				expressionToBuild.SpanStart,
				SyntaxFactory.IdentifierName(typeName),
				SpeculativeBindingOption.BindAsTypeOrNamespace
			).Symbol;
			if (stringConversionTypeSymbol == null)
			{
				// Add "using Godot;" directive
				newRoot = compilationUnit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("Godot")));
			}
		}

		return document.WithSyntaxRoot(newRoot);
	}
}
