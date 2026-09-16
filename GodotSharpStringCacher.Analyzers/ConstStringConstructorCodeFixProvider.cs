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
		SemanticModel? semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
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
				createChangedDocument: ct => RemoveExplicitConstructorAsync(context.Document, semanticModel, typeName, objectCreationExpression, ct),
				equivalenceKey: "GDStringTypeRemoveCtor"),
			context.Diagnostics
		);
	}

	static async Task<Document> RemoveExplicitConstructorAsync(Document document, SemanticModel semanticModel,
		string typeName, BaseObjectCreationExpressionSyntax expressionToBuild, CancellationToken ct)
	{
		bool needsUsing = false;
		ExpressionSyntax replacementExpression = ReplaceExpression(
			expressionToBuild, semanticModel, typeName, ref needsUsing, ct);

		SyntaxNode oldRoot = (await document.GetSyntaxRootAsync(ct).ConfigureAwait(false))!;
		SyntaxNode newRoot = oldRoot.ReplaceNode(expressionToBuild, replacementExpression);
		if (needsUsing)
		{
			newRoot = AddUsingIfNecessary(newRoot, semanticModel, typeName, expressionToBuild.SpanStart);
		}

		return document.WithSyntaxRoot(newRoot);
	}

	static ExpressionSyntax ReplaceExpression(BaseObjectCreationExpressionSyntax expressionToReplace,
		SemanticModel semanticModel, string typeName, ref bool needsUsing, CancellationToken ct)
	{
		ExpressionSyntax replacement = expressionToReplace.ArgumentList!.Arguments[0].Expression;

		TypeInfo typeInfo = semanticModel.GetTypeInfo(expressionToReplace, ct);

		if (typeInfo.Type != null && typeInfo.ConvertedType != null && !SymbolEqualityComparer.Default.Equals(typeInfo.Type, typeInfo.ConvertedType)) {
			needsUsing = true;
			replacement = SyntaxFactory.CastExpression(
				type: SyntaxFactory.IdentifierName(typeName),
				expression: replacement
			);
		}

		return replacement;
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
		if (root == null)
			return null;
		SemanticModel? semanticModel = await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
		if (semanticModel == null)
			return null;

		Dictionary<BaseObjectCreationExpressionSyntax, string> expressionsToReplace = new(diagnostics.Length);

		foreach (Diagnostic diagnostic in diagnostics)
		{
			SyntaxNode syntaxNode = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
			if (syntaxNode is BaseObjectCreationExpressionSyntax toRemove)
			{
				expressionsToReplace.Add(toRemove, diagnostic.Properties["typeName"]!);
			}
		}

		bool needsUsing = false;

		SyntaxNode newRoot = root.ReplaceNodes(
			expressionsToReplace.Keys,
			(original, current) => ReplaceExpression(
				current,
				semanticModel,
				expressionsToReplace[original],
				ref needsUsing,
				context.CancellationToken)
		);

		if (needsUsing)
		{
			newRoot = AddUsingIfNecessary(newRoot,
				semanticModel,
				// Since StringName and NodePath are in the same namespace, it doesn't matter which one is chosen
				diagnostics[0].Properties["typeName"]!,
				diagnostics[0].Location.SourceSpan.Start);
		}

		return document.WithSyntaxRoot(newRoot);
	}
}
