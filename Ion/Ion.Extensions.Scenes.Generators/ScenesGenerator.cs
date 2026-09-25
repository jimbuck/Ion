using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Ion.Generators;

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
		// The enum overloads (UseScene(Scene, ...) and EmitChangeScene(Scene)) used to be generated here. They are now the
		// generic UseScene<TScene> and EmitChangeScene<TScene> of Ion.Extensions.Scenes, so that other generators (the
		// schedule generator, which cannot see this generator's output) bind scene registrations and their callbacks.
		_ = compilation;
		_ = enums;
		_ = context;
	}
}