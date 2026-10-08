using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GodotSharpStringCacher.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
class ForeachImplicitConversionAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Common.ImplicitStringTypeConversionInForeachRule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeForeachSyntax, SyntaxKind.ForEachStatement);
		context.RegisterSyntaxNodeAction(AnalyzeForeachVariableSyntax, SyntaxKind.ForEachVariableStatement);
	}

	void AnalyzeForeachSyntax(SyntaxNodeAnalysisContext context)
	{
		SemanticModel semanticModel = context.SemanticModel;

		if (context.Node is ForEachStatementSyntax syntax)
		{
			ForEachStatementInfo forEachInfo = semanticModel.GetForEachStatementInfo(syntax);
			
			if (forEachInfo.ElementType is { SpecialType: SpecialType.System_String })
			{
				TypeInfo type = semanticModel.GetTypeInfo(syntax.Type);
				if (type.Type is
					{
						ContainingAssembly.Name: "GodotSharp",
						Name: string ctorTypeName and ("StringName" or "NodePath")
					})
				{
					context.ReportDiagnostic(Diagnostic.Create(
						Common.ImplicitStringTypeConversionInForeachRule,
						syntax.Type.GetLocation(),
						ImmutableDictionary.CreateRange<string, string?>([new("typeName", ctorTypeName)]),
						ctorTypeName));
				}
			}
		}
	}

	/// <summary>
	/// We're handling this type of syntax here:
	/// <code>
	/// ((string, (bool, string)), int)[] arr = ...;
	/// foreach (((StringName n, (bool e, NodePath u)), int x) in arr)
	/// {
	///     ...
	/// }
	/// </code>
	/// </summary>
	void AnalyzeForeachVariableSyntax(SyntaxNodeAnalysisContext context)
	{
		SemanticModel semanticModel = context.SemanticModel;

		if (context.Node is ForEachVariableStatementSyntax syntax && syntax.Variable is TupleExpressionSyntax tupleConstruction)
		{
			DeconstructionInfo rootDeconstruction = semanticModel.GetDeconstructionInfo(syntax);

			List<(DeclarationExpressionSyntax, string)>? declarationsToReport = null;

			// See docstring of DeconstructionInfo for detailed info about how it works.
			// Unfortunately, DeconstructionInfo does not store any Node information, so we
			// have to manually follow along with the TupleExpressionSyntax
			void HandleDeconstructionTree(DeconstructionInfo dec, ExpressionSyntax correspondingDeclaration)
			{
				if (!dec.Nested.IsEmpty)
				{
					if (correspondingDeclaration is not TupleExpressionSyntax tupleExpression)
					{
						// Shouldn't happen
						return;
					}
					for (int i = 0; i < dec.Nested.Length; i++)
					{
						DeconstructionInfo nestedDeconstruction = dec.Nested[i];
						ArgumentSyntax nestedDeclaration = tupleExpression.Arguments[i];
						HandleDeconstructionTree(nestedDeconstruction, nestedDeclaration.Expression);
					}
				}
				else
				{
					if (dec.Conversion is
						{
							IsImplicit: true,
							MethodSymbol:
							{
								ContainingType:
								{
									ContainingAssembly.Name: "GodotSharp",
									Name: string containingTypeName and ("StringName" or "NodePath")
								},
								ReturnType:
								{
									ContainingAssembly.Name: "GodotSharp",
									Name: string ctorTypeName and ("StringName" or "NodePath")
								},
								Parameters: { Length: 1 } args
							},
						} && containingTypeName == ctorTypeName && args[0].Type.SpecialType == SpecialType.System_String)
					{
						if (correspondingDeclaration is DeclarationExpressionSyntax declaration)
						{
							// Should always happen ?
							declarationsToReport ??= [];
							declarationsToReport.Add((declaration, ctorTypeName));
						}
					}
				}
			}

			HandleDeconstructionTree(rootDeconstruction, tupleConstruction);

			if (declarationsToReport is not null)
			{
				foreach ((DeclarationExpressionSyntax decl, string typeName) in declarationsToReport)
				{
					context.ReportDiagnostic(Diagnostic.Create(
						Common.ImplicitStringTypeConversionInForeachRule,
						decl.GetLocation(),
						ImmutableDictionary.CreateRange<string, string?>([new("typeName", typeName)]),
						typeName));
				}
			}
		}
	}
}
