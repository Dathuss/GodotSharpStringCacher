using System;
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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ForeachImplicitConversionCodeFixProvider)), Shared]
public sealed class ForeachImplicitConversionCodeFixProvider : CodeFixProvider
{
	public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Common.ImplicitStringTypeConversionInForeachRule.Id);

	private static readonly FixAllProvider _fixAll = FixAllProvider.Create(FixAllAsync);
	public override FixAllProvider? GetFixAllProvider() => _fixAll;

	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root == null)
			return;

		Diagnostic diagnostic = context.Diagnostics.First();

		TextSpan diagnosticSpan = diagnostic.Location.SourceSpan;

		if (root.FindNode(diagnosticSpan, getInnermostNodeForTie: false) is not TypeSyntax typeSyntax)
			return;
		
		if (GetOuterForeachStatement(typeSyntax) is not {} forEachStatement)
			return;

		context.RegisterCodeFix(
			CodeAction.Create(
				title: "Add explicit cast inside foreach block",
				createChangedDocument: ct => FixForeachAsync(context.Document, root, typeSyntax, forEachStatement, ct),
				equivalenceKey: "GDStringTypeExplicitForeach"),
			context.Diagnostics
		);
	}

	static async Task<Document> FixForeachAsync(Document document, SyntaxNode oldRoot,
		TypeSyntax typeSyntax, CommonForEachStatementSyntax forEachStatement, CancellationToken ct)
	{
		SyntaxNode newRoot = oldRoot.ReplaceNode(forEachStatement, MoveCastsInLoop(forEachStatement, [typeSyntax]));

		return document.WithSyntaxRoot(newRoot);
	}

	static CommonForEachStatementSyntax? GetOuterForeachStatement(SyntaxNode syntaxNode)
	{
		while (syntaxNode.Parent != null)
		{
			if (syntaxNode.Parent is CommonForEachStatementSyntax found)
			{
				return found;
			}
			syntaxNode = syntaxNode.Parent;
		}
		return null;
	}

	/// <summary>
	/// Turns either
	/// <code>
	/// foreach (StringName x in ...)
	/// 	...
	/// </code>
	/// or
	/// <code>
	/// foreach ((int x, (int y, NodePath z), ... StringName w) in ...)
	/// 	...
	/// </code>
	/// (any tuple form is supported)<br/>
	/// into
	/// <code>
	/// foreach (string xString in ...)
	/// {
	/// 	StringName x = new(xString);
	/// 	...
	/// }
	/// </code>
	/// or
	/// <code>
	/// foreach ((int x, (int y, string zString), ... string wString) in ...)
	/// {
	/// 	NodePath z = new(zString);
	/// 	StringName w = new(wString);
	/// 	...
	/// }
	/// </code>
	/// </summary>
	/// <param name="typeSyntaxes">If using a "complex" foreach, is the list of all type expressions inside the tuple expression to replace</param>
	/// <returns></returns>
	static CommonForEachStatementSyntax MoveCastsInLoop(CommonForEachStatementSyntax syntax, TypeSyntax[]? typeSyntaxes = null)
	{
		static BlockSyntax PrependStatementsToLoop(IEnumerable<StatementSyntax> statements, StatementSyntax loop)
		{
			if (loop is BlockSyntax blockSyntax)
			{
				// Using blockSyntax.WithStatements breaks trivia (indentation)
				// of the line we add, so we create a new BlockSyntax instead
				return SyntaxFactory.Block(
					blockSyntax.AttributeLists,
					SyntaxFactory.List(statements).AddRange(blockSyntax.Statements)
				);
			}
			else
			{
				// We are in a case with this syntax:
				// foreach (...)
				//     DoSomething();
				// Create a new block with our declaration and the loop content
				return SyntaxFactory.Block(
					SyntaxFactory.List(statements).Add(loop)
				);
			}
		}

		static StatementSyntax MakeDeclaration(TypeSyntax typeSyntax, SyntaxToken variableName, SyntaxToken replacementName)
		{
			// This is the parsing equivalent of
			// typeSyntax replacementName = new(variableName);
			return SyntaxFactory.LocalDeclarationStatement(
				SyntaxFactory.VariableDeclaration(typeSyntax,
					SyntaxFactory.SingletonSeparatedList(
						SyntaxFactory.VariableDeclarator(
							variableName,
							null,
							SyntaxFactory.EqualsValueClause(
								SyntaxFactory.ImplicitObjectCreationExpression(
									SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
										SyntaxFactory.Argument(SyntaxFactory.IdentifierName(replacementName))
									)),
									null
								)
							)
						)
					)
				)
			);
		}

		PredefinedTypeSyntax nativeString = SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword));

		if (syntax is ForEachStatementSyntax forEachStatement)
		{
			SyntaxToken originalName = forEachStatement.Identifier;
			SyntaxToken replacementName = SyntaxFactory.Identifier(originalName + "String");
			StatementSyntax declaration = MakeDeclaration(forEachStatement.Type, originalName, replacementName);

			return forEachStatement
				.WithType(nativeString)
				.WithIdentifier(replacementName)
				.WithStatement(PrependStatementsToLoop([declaration], forEachStatement.Statement));
		}
		else if (syntax is ForEachVariableStatementSyntax forEachVariable)
		{
			if (typeSyntaxes == null)
				throw new ArgumentNullException(nameof(typeSyntaxes));

			List<StatementSyntax> statementsToPrepend = new(typeSyntaxes.Length);
			Dictionary<DeclarationExpressionSyntax, DeclarationExpressionSyntax> declarationsToReplace = new(typeSyntaxes.Length);

			foreach (TypeSyntax typeSyntax in typeSyntaxes)
			{
				// Get the variable declaration associated with the type expression
				DeclarationExpressionSyntax declarationSyntax = (DeclarationExpressionSyntax)typeSyntax.Parent!;
				SingleVariableDesignationSyntax designationSyntax = (SingleVariableDesignationSyntax)declarationSyntax.Designation;

				SyntaxToken originalName = designationSyntax.Identifier;
				SyntaxToken replacementName = SyntaxFactory.Identifier(originalName + "String");
				StatementSyntax newDeclaration = MakeDeclaration(typeSyntax, originalName, replacementName);

				DeclarationExpressionSyntax replacementDeclaration = SyntaxFactory.DeclarationExpression(
					nativeString,
					SyntaxFactory.SingleVariableDesignation(replacementName)
				);

				statementsToPrepend.Add(newDeclaration);
				declarationsToReplace.Add(declarationSyntax, replacementDeclaration);
			}

			return forEachVariable
				.WithStatement(PrependStatementsToLoop(statementsToPrepend, forEachVariable.Statement))
				.WithVariable(forEachVariable.Variable.ReplaceNodes(
					declarationsToReplace.Keys,
					(original, current) => declarationsToReplace[original]
				));
		}
		else
		{
			throw new NotImplementedException($"Unknown foreach syntax type `{syntax.GetType()}`");
		}
	}

	static async Task<Document?> FixAllAsync(FixAllContext context, Document document, ImmutableArray<Diagnostic> diagnostics)
	{
		SyntaxNode? root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root == null)
			return null;
		SemanticModel? semanticModel = await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
		if (semanticModel == null)
			return null;

		Dictionary<CommonForEachStatementSyntax, List<TypeSyntax>> expressionsToReplace = new(diagnostics.Length);

		foreach (var diagnostic in diagnostics)
		{
			if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: false) is TypeSyntax typeSyntax
				&& GetOuterForeachStatement(typeSyntax) is {} forEachStatement)
			{
				if (!expressionsToReplace.ContainsKey(forEachStatement))
				{
					expressionsToReplace.Add(forEachStatement, [typeSyntax]);
				}
				else
				{
					expressionsToReplace[forEachStatement].Add(typeSyntax);
				}
			}
		}

		SyntaxNode newRoot = root.ReplaceNodes(
			expressionsToReplace.Keys,
			(original, current) => MoveCastsInLoop(current, expressionsToReplace[original].ToArray())
		);

		return document.WithSyntaxRoot(newRoot);
	}
}
