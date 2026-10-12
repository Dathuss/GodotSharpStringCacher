using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GodotSharpStringCacher.Analyzers;

public static class Common
{
	internal static readonly DiagnosticDescriptor StringTypeImplicitOperatorWithNonConstantStringRule = new(
		id: "GDS001",
		title: "Implicitly allocating StringName/NodePath from non-constant string, which should be explicit",
		messageFormat: "Implicitly allocating {0} from non-constant string, which should be explicit",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "When creating a StringName or NodePath from a non-constant string, prefer using \"new StringName\" or \"new NodePath\" to make the allocation explicit."
	);

	internal static readonly DiagnosticDescriptor StringTypeConstructorWithConstantStringRule = new(
		id: "GDS002",
		title: "Unnecessarily constructing StringName/NodePath from constant string, which could be cached",
		messageFormat: "Unnecessarily constructing {0} from constant string, which could be cached",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Consider removing the constructor in order to statically cache the StringName or NodePath."
	);

	internal static readonly DiagnosticDescriptor ImplicitStringTypeConversionInForeachRule = new(
		id: "GDS003",
		title: "Implicitly allocating StringName/NodePath in foreach loop",
		messageFormat: "Implicitly allocating {0} in foreach loop, which should be explicit",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "This foreach syntax is implicitly allocating a StringName or NodePath for each iteration. Consider making the allocation explicit or converting the enumerable beforehand."
	);

	internal static SyntaxNode AddUsingIfNecessary(SyntaxNode root, SemanticModel semanticModel, string typeName, int currentSpan, string @using = "Godot")
	{
		if (root is CompilationUnitSyntax compilationUnit)
		{
			// Check if the control symbol is accessible
			ISymbol? stringTypeSymbol = semanticModel.GetSpeculativeSymbolInfo(
				currentSpan,
				SyntaxFactory.IdentifierName(typeName),
				SpeculativeBindingOption.BindAsTypeOrNamespace
			).Symbol;
			if (stringTypeSymbol == null)
			{
				// Add "using ...;" directive
				root = compilationUnit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName(@using)));
			}
		}
		return root;
	}
}
