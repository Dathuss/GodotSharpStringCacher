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
	public override ImmutableArray<string> FixableDiagnosticIds
		=> ImmutableArray.Create(Common.StringTypeConstructorWithConstantStringRule.Id);

	public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		SemanticModel? semanticModel = await context.Document.GetSemanticModelAsync().ConfigureAwait(false);
		if (root == null || semanticModel == null)
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
				title: $"Remove {typeName} constructor",
				createChangedDocument: ct => RemoveExplicitConstructorAsync(context.Document, objectCreationExpression, ct),
				equivalenceKey: $"{typeName}_removeCtor"),
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
}
