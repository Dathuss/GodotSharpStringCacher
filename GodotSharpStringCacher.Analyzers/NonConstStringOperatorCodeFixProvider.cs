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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConstStringCodeFixProvider))]
public sealed class ConstStringCodeFixProvider : CodeFixProvider
{
	public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(NonConstStringOperatorAnalyzer._rule.Id);

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

		ExpressionSyntax expression;

		if (syntaxNode is ExpressionSyntax syntax)
		{
			// Example:'NodePath nodePath = NetworkPacket.StringValue;'
			// Here 'NetworkPacket.StringValue' is selected.
			expression = syntax;
		}
		else if (syntaxNode is ArgumentSyntax argumentSyntax)
		{
			// Example: 'return GetNodeOrNull(NetworkPacket.StringValue);'
			// Here 'NetworkPacket.StringValue' is selected.
			expression = argumentSyntax.Expression;
		}
		else
		{
			return;
		}

		ITypeSymbol? convertedType = semanticModel.GetTypeInfo(expression, context.CancellationToken).ConvertedType;

		if (convertedType == null)
			return;

		string stringConversionType = convertedType.Name;

		context.RegisterCodeFix(
			CodeAction.Create(
				$"Add explicit {stringConversionType} constructor",
				ct => AddExplicitConstructorAsync(context.Document, stringConversionType, expression, ct),
				stringConversionType
			),
			context.Diagnostics
		);
	}

	private static async Task<Document> AddExplicitConstructorAsync(Document document,
		string stringConversionType, ExpressionSyntax expressionToBuild, CancellationToken ct)
	{
		// From any expression 'expr', replace it with 'new StringName(expr)' (or 'new NodePath(expr)')
		ObjectCreationExpressionSyntax objectCreationExpression = SyntaxFactory.ObjectCreationExpression(
			SyntaxFactory.IdentifierName(stringConversionType),
			SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
				SyntaxFactory.Argument(expressionToBuild)
			)),
			null
		);

		SyntaxNode oldRoot = (await document.GetSyntaxRootAsync(ct).ConfigureAwait(false))!;
		SyntaxNode newRoot = oldRoot.ReplaceNode(expressionToBuild, objectCreationExpression);
		if (newRoot is CompilationUnitSyntax compilationUnit)
		{
			// Add 'using Godot;' directive if it does not exist
			if (!compilationUnit.Usings.Any(x => x.Name.ToString() == "Godot"))
			{
				newRoot = compilationUnit.AddUsings([SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("Godot"))]);
			}
		}

		return document.WithSyntaxRoot(newRoot);
	}
}
