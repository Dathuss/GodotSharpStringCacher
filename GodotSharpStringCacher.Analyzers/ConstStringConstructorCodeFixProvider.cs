using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace GodotSharpStringCacher.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConstStringConstructorCodeFixProvider)), Shared]
public sealed class ConstStringConstructorCodeFixProvider : CodeFixProvider
{
	public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Common.StringTypeConstructorWithConstantStringRule.Id);

	// We cannot use WellKnownFixAllProviders.BatchFixer because it does not work when
	// diagnostics have spans that overlap, which is possible with this code rule
	// because we handle ternaries.
	// https://github.com/dotnet/roslyn/blob/main/docs/analyzers/FixAllProvider.md#limitations-of-the-batchfixer
	private static readonly FixAllProvider _fixAll = FixAllProvider.Create(FixAllAsync);
	public override FixAllProvider? GetFixAllProvider() => _fixAll;

	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root == null)
			return;
		SemanticModel? semanticModel = await context.Document.GetSemanticModelAsync().ConfigureAwait(false);
		if (semanticModel == null)
			return;

		Diagnostic diagnostic = context.Diagnostics.First();

		TextSpan diagnosticSpan = diagnostic.Location.SourceSpan;

		SyntaxNode syntaxNode = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);

		if (syntaxNode is not BaseObjectCreationExpressionSyntax objectCreationExpression)
			return;

		// Guaranteed to be either "StringName" or "NodePath"
		string typeName = diagnostic.Properties["typeName"]!;

		context.RegisterCodeFix(
			CodeAction.Create(
				title: "Remove constructor",
				createChangedDocument: ct => RemoveExplicitConstructorAsync(context.Document, objectCreationExpression, ct),
				equivalenceKey: "GDStringTypeRemoveCtor"),
			context.Diagnostics
		);
	}

	static async Task<Document> RemoveExplicitConstructorAsync(Document document,
		BaseObjectCreationExpressionSyntax objectCreationExpression, CancellationToken ct)
	{
		ExpressionSyntax argumentExpression = objectCreationExpression.ArgumentList!.Arguments[0].Expression;
		SyntaxNode oldRoot = (await document.GetSyntaxRootAsync(ct).ConfigureAwait(false))!;
		SyntaxNode newRoot = oldRoot.ReplaceNode(objectCreationExpression, argumentExpression);

		return document.WithSyntaxRoot(newRoot);
	}

	static async Task<Document?> FixAllAsync(FixAllContext context, Document document, ImmutableArray<Diagnostic> diagnostics)
	{
		SyntaxNode? root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root == null)
			return null;

		List<BaseObjectCreationExpressionSyntax> expressionsToReplace = new(diagnostics.Length);

		foreach (Diagnostic diagnostic in diagnostics)
		{
			SyntaxNode syntaxNode = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
			if (syntaxNode is BaseObjectCreationExpressionSyntax toRemove)
			{
				expressionsToReplace.Add(toRemove);
			}
		}

		SyntaxNode newRoot = root.ReplaceNodes(
			expressionsToReplace,
			(_, current) => current.ArgumentList!.Arguments[0].Expression
		);

		return document.WithSyntaxRoot(newRoot);
	}
}
