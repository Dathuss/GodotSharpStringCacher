
using Microsoft.CodeAnalysis;

namespace GodotSharpStringCacher.Analyzers;

public static class Common
{
	internal static readonly DiagnosticDescriptor StringTypeImplicitOperatorWithNonConstantStringRule = new(
		id: "GDS001",
		title: "Implicitly allocating StringName/NodePath from non-constant string",
		messageFormat: "Implicitly allocating {0} from non-constant string",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "When creating a StringName or NodePath from a non-constant string, prefer using \"new StringName\" or \"new NodePath\" to make the allocation explicit."
	);

	internal static readonly DiagnosticDescriptor StringTypeConstructorWithConstantStringRule = new(
		id: "GDS002",
		title: "Using StringName/NodePath constructor with constant string",
		messageFormat: "Using {0} constructor with constant string",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Consider removing the constructor in order to statically cache the StringName or NodePath."
	);
}
