using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceGeneratorUtils;

using System.Diagnostics;
using System.Collections.Immutable;

namespace Ion.Extensions.Scenes.Generators;

[Generator]
public class ScenesGenerator : IIncrementalGenerator
{
	private static readonly string CONTAINING_NAMESPACE = "Ion.Extensions.Scenes";
	private static readonly string SCENE_ATTRIBUTE_NAME = "ScenesEnumAttribute";
	private static readonly string[] VALID_SCENE_ENUM_NAMES = ["Scene", "Scenes"];

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
#if DEBUGGENERATORS
		if (!Debugger.IsAttached)
		{
			Debugger.Launch();
		}
#endif

		Debug.WriteLine($"{nameof(ScenesGenerator)}.{nameof(Initialize)}");

		Debug.WriteLine($"{nameof(ScenesGenerator)} CreateAttributes");

		// Add the marker attribute to the compilation
		context.RegisterPostInitializationOutput(ctx => ctx.AddSource($"{SCENE_ATTRIBUTE_NAME}.g.cs", _getScenesAttribute()));


		IncrementalValuesProvider<EnumDeclarationSyntax> enumDeclarations = context.SyntaxProvider
			.CreateSyntaxProvider(
				predicate: static (s, _) => _isSyntaxTargetForGeneration(s),
				transform: static (ctx, _) => _getSemanticTargetForGeneration(ctx))
			.Where(static m => m is not null)!; // filter out attributed enums that we don't care about

		Debug.WriteLine($"{nameof(ScenesGenerator)} Extensions");
		// Combine the selected classes with the `Compilation`
		IncrementalValueProvider<(Compilation, ImmutableArray<EnumDeclarationSyntax>)> compilationAndEnums = context.CompilationProvider.Combine(enumDeclarations.Collect());
		context.RegisterSourceOutput(compilationAndEnums, static (spc, source) => _execute(source.Item1, source.Item2, spc));
	}

	private static SourceText _getScenesAttribute()
	{
		return SourceText.From($@"
namespace {CONTAINING_NAMESPACE};

[AttributeUsage(AttributeTargets.Enum, AllowMultiple = false)]
public class {SCENE_ATTRIBUTE_NAME} : Attribute {{ }}
", System.Text.Encoding.UTF8);
	}

	static bool _isSyntaxTargetForGeneration(SyntaxNode node)
	{
		if (node is not EnumDeclarationSyntax e) return false;
		
		if (VALID_SCENE_ENUM_NAMES.Contains(e.Identifier.Text)) return true;
		
		return e.AttributeLists.Count > 0;
	}

	static EnumDeclarationSyntax? _getSemanticTargetForGeneration(GeneratorSyntaxContext context)
	{
		var enumDeclarationSyntax = (EnumDeclarationSyntax)context.Node;

		if (VALID_SCENE_ENUM_NAMES.Contains(enumDeclarationSyntax.Identifier.Text)) return enumDeclarationSyntax;

		foreach (AttributeListSyntax attributeListSyntax in enumDeclarationSyntax.AttributeLists)
		{
			foreach (AttributeSyntax attributeSyntax in attributeListSyntax.Attributes)
			{
				var symbolInfo = context.SemanticModel.GetSymbolInfo(attributeSyntax);
				List<ISymbol?> symbols = [symbolInfo.Symbol, ..symbolInfo.CandidateSymbols];

				foreach(var symbol in symbols)
				{
					if (symbol is not ISymbol attributeSymbol) continue;

					INamedTypeSymbol attributeContainingTypeSymbol = attributeSymbol.ContainingType;

					if (attributeContainingTypeSymbol.ContainingNamespace.ToDisplayString() == CONTAINING_NAMESPACE && attributeContainingTypeSymbol.Name == SCENE_ATTRIBUTE_NAME) return enumDeclarationSyntax;
				}
			}
		}

		return null;
	}

	static void _execute(Compilation compilation, ImmutableArray<EnumDeclarationSyntax> enums, SourceProductionContext context)
	{
		if (enums.IsDefaultOrEmpty) return;

		IEnumerable<EnumDeclarationSyntax> distinctEnums = enums.Distinct();

		foreach(var enumDeclarationSyntax in distinctEnums)
		{
			SemanticModel semanticModel = compilation.GetSemanticModel(enumDeclarationSyntax.SyntaxTree);

			var enumSymbol = semanticModel.GetDeclaredSymbol(enumDeclarationSyntax);

			if (enumSymbol is null) continue;

			var enumNamespace = enumSymbol.ContainingNamespace.ToDisplayString();
			var enumName = enumSymbol.Name;

			var sourceText = _createExtensionMethods(enumNamespace, enumName);
			context.AddSource($"{enumSymbol.Name}SceneExtensions.g.cs", sourceText);
		}
	}

	static SourceText _createExtensionMethods(string enumNamespace, string enumName)
	{
		var source = new SourceWriter();

		source.WriteLine($"namespace Ion.Extensions.Scenes;");

		source.WriteLine($"public static class {enumName}SceneExtensions");
		source.OpenBlock();

		#region UseScene Extension
		source.WriteLine($"public static IIonApplication UseScene(this IIonApplication app, {enumNamespace}.{enumName} sceneId, Action<ISceneBuilder> configure) => return app.UseScene((int)sceneId, configure);");
		#endregion

		source.WriteEmptyLines(1);

		#region EmitChangeScene Extension
		source.WriteLine($"public static void EmitChangeScene(this IEventEmitter eventEmitter, {enumNamespace}.{enumName} nextSceneId) => eventEmitter.EmitChangeScene((int)nextSceneId);");
		#endregion

		source.CloseBlock();

		return source.ToSourceText();
	}
}