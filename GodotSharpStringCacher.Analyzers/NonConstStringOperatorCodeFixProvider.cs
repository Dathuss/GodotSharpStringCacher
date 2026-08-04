using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NonConstStringOperatorCodeFixProvider)), Shared]
public sealed class NonConstStringOperatorCodeFixProvider : CodeFixProvider
{
	public override ImmutableArray<string> FixableDiagnosticIds
		=> ImmutableArray.Create(Common.StringTypeImplicitOperatorWithNonConstantStringRule.Id);

	// We cannot use WellKnownFixAllProviders.BatchFixer because it does not work when
	// diagnostics have spans that overlap, which is possible with this code rule.
	// https://github.com/dotnet/roslyn/blob/main/docs/analyzers/FixAllProvider.md#limitations-of-the-batchfixer
	private static readonly FixAllProvider _fixAll = FixAllProvider.Create(FixAllAsync);
	public override FixAllProvider? GetFixAllProvider() => _fixAll;

	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		SemanticModel? semanticModel = await context.Document.GetSemanticModelAsync().ConfigureAwait(false);
		if (root == null || semanticModel == null)
			return;

		Diagnostic diagnostic = context.Diagnostics.First();

		TextSpan diagnosticSpan = diagnostic.Location.SourceSpan;

		SyntaxNode syntaxNode = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);

		if (syntaxNode is not ExpressionSyntax expression)
			return;

		// Guaranteed to be either "StringName" or "NodePath"
		string typeName = diagnostic.Properties["typeName"]!;

		context.RegisterCodeFix(
			CodeAction.Create(
				title: $"Add explicit {typeName} constructor",
				createChangedDocument: ct => AddExplicitConstructorAsync(context.Document, semanticModel, typeName, expression, ct),
				equivalenceKey: $"{typeName}_addCtor"),
			context.Diagnostics
		);
	}

	static async Task<Document> AddExplicitConstructorAsync(Document document, SemanticModel semanticModel,
		string typeName, ExpressionSyntax expressionToBuild, CancellationToken ct)
	{
		ExpressionSyntax replacementExpression = ReplaceExpression(
			expressionToBuild, semanticModel, typeName, ct);

		SyntaxNode oldRoot = (await document.GetSyntaxRootAsync(ct).ConfigureAwait(false))!;
		SyntaxNode newRoot = oldRoot.ReplaceNode(expressionToBuild, replacementExpression);
		newRoot = AddUsingIfNecessary(newRoot, semanticModel, typeName, expressionToBuild.SpanStart);

		return document.WithSyntaxRoot(newRoot);
	}

	static ExpressionSyntax ReplaceExpression(ExpressionSyntax expressionToReplace, SemanticModel semanticModel, string typeName, CancellationToken ct)
	{
		// Remove explicit cast to StringName/NodePath if present
		ExpressionSyntax expressionInsideConstructor = expressionToReplace;
		if (expressionToReplace is CastExpressionSyntax castExpression)
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
		return objectCreationExpression;
	}

	static SyntaxNode AddUsingIfNecessary(SyntaxNode root, SemanticModel semanticModel, string typeName, int currentSpan)
	{
		if (root is CompilationUnitSyntax compilationUnit)
		{
			// Check if the symbol "StringName"/"NodePath" is accessible
			ISymbol? stringTypeSymbol = semanticModel.GetSpeculativeSymbolInfo(
				currentSpan,
				SyntaxFactory.IdentifierName(typeName),
				SpeculativeBindingOption.BindAsTypeOrNamespace
			).Symbol;
			if (stringTypeSymbol == null)
			{
				// Add "using Godot;" directive
				root = compilationUnit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("Godot")));
			}
		}
		return root;
	}

	static async Task<Document?> FixAllAsync(FixAllContext context, Document document, ImmutableArray<Diagnostic> diagnostics)
	{
		SyntaxNode? root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		SemanticModel? semanticModel = await document.GetSemanticModelAsync().ConfigureAwait(false);
		if (root == null || semanticModel == null)
			return null;

		string typeName = diagnostics.First().Properties["typeName"]!;

		List<ExpressionSyntax> expressionsToReplace = new(diagnostics.Length);

		foreach (Diagnostic d in diagnostics)
		{
			SyntaxNode syntaxNode = root.FindNode(d.Location.SourceSpan, getInnermostNodeForTie: true);
			if (syntaxNode is ExpressionSyntax toReplace)
			{
				expressionsToReplace.Add(toReplace);
			}
		}

		SyntaxNode newRoot = root.ReplaceNodes(
			expressionsToReplace,
			(_, current) => ReplaceExpression(current, semanticModel, typeName, context.CancellationToken)
		);

		newRoot = AddUsingIfNecessary(newRoot, semanticModel, typeName, expressionsToReplace[0].SpanStart);

		return document.WithSyntaxRoot(newRoot);
	}
}
