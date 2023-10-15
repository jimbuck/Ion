using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace Kyber.Generators;

[Generator]
public class GameGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
#if DEBUG
		if (!Debugger.IsAttached)
		{
			Debugger.Launch();
		}
#endif

		Debug.WriteLine($"{nameof(GameGenerator)}.{nameof(Initialize)}");

		Debug.WriteLine($"{nameof(GameGenerator)}.CreateAttributes");
		// Add the marker attribute to the compilation
		context.RegisterPostInitializationOutput(ctx => ctx.AddSource("KyberAttributes.g.cs", SourceText.From(SourceGenerationHelper.Attributes, Encoding.UTF8)));

		Debug.WriteLine($"{nameof(GameGenerator)}.GetClasses");
		// Do a simple filter for classes
		IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations = context.SyntaxProvider
			.CreateSyntaxProvider(
				predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
				transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
			.Where(static m => m is not null)!; // filter out attributed enums that we don't care about

		Debug.WriteLine($"{nameof(GameGenerator)}.CombineClasess");
		// Combine the selected classes with the `Compilation`
		IncrementalValueProvider<(Compilation, ImmutableArray<ClassDeclarationSyntax>)> compilationAndEnums = context.CompilationProvider.Combine(classDeclarations.Collect());

		Debug.WriteLine($"{nameof(GameGenerator)}.CreateScenes");
		context.RegisterSourceOutput(compilationAndEnums, static (spc, source) => Execute(source.Item1, source.Item2, spc));
	}

	static bool IsSyntaxTargetForGeneration(SyntaxNode node)
	{
		if (node is not ClassDeclarationSyntax c) return false; 
		var isTarget = c.AttributeLists.Count > 0;

		return isTarget;
	}

	static ClassDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
	{
		var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;

		foreach (AttributeListSyntax attributeListSyntax in classDeclarationSyntax.AttributeLists)
		{
			foreach (AttributeSyntax attributeSyntax in attributeListSyntax.Attributes)
			{
				var symbol = context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol;
				if (symbol is not ISymbol attributeSymbol) continue;

				INamedTypeSymbol attributeContainingTypeSymbol = attributeSymbol.ContainingType;

				if (attributeContainingTypeSymbol.ContainingNamespace.Name == "Kyber" && attributeContainingTypeSymbol.Name == "SystemAttribute") return classDeclarationSyntax;
			}
		}

		return null;
	}

	static void Execute(Compilation compilation, ImmutableArray<ClassDeclarationSyntax> classes, SourceProductionContext context)
	{
		if (classes.IsDefaultOrEmpty) return;

		IEnumerable<ClassDeclarationSyntax> distinctClasses = classes.Distinct();

		// Convert each EnumDeclarationSyntax to an EnumToGenerate
		List<SceneClass> scenesToGenerate = GetTypesToGenerate(compilation, distinctClasses, context.CancellationToken);

		// generate the source code and add it to the output
		foreach (var scene in scenesToGenerate)
		{
			var source = SourceGenerationHelper.GenerateSceneClass(scene);
			context.AddSource($"{scene.ClassName}.g.cs", source);
		}
	}

	static List<SceneClass> GetTypesToGenerate(Compilation compilation, IEnumerable<ClassDeclarationSyntax> classes, CancellationToken ct)
	{
		// Create a list to hold our output
		var scenesToGenerate = new List<SceneClass>();
		// Get the semantic representation of our marker attribute 
		INamedTypeSymbol? systemAttribute = compilation.GetTypeByMetadataName("Kyber.SystemAttribute`1");

		if (systemAttribute == null) return scenesToGenerate;

		foreach (var sceneClassDeclarationSyntax in classes)
		{
			// stop if we're asked to
			ct.ThrowIfCancellationRequested();

			SemanticModel semanticModel = compilation.GetSemanticModel(sceneClassDeclarationSyntax.SyntaxTree);
			if (semanticModel.GetDeclaredSymbol(sceneClassDeclarationSyntax) is not INamedTypeSymbol sceneClassSymbol) continue;

			string sceneClassNamespace = sceneClassSymbol.ContainingNamespace.ToString();
			string sceneClassName = sceneClassSymbol.Name;

			var systemAttributes = sceneClassSymbol.GetAttributes().Where(a => a.AttributeClass?.MetadataName == "Kyber.SystemAttribute`1").ToList();

			// TODO: Get each registered system then loop over them...
			var systemSymbols = new List<ISymbol>();

			var systems = new List<SystemClass>();
			var updateCalls = new List<LifecycleMethodCall>();
			var drawCalls = new List<LifecycleMethodCall>();

			foreach (var systemSymbol in systemSymbols)
			{
				var systemClass = new SystemClass(sceneClassNamespace, sceneClassName, SourceGenerationHelper.ToPrivateName(sceneClassName));
				systems.Add(systemClass);

				ImmutableArray<ISymbol> classMembers = sceneClassSymbol.GetMembers();

				// Get all the fields from the enum, and add their name to the list
				foreach (ISymbol member in classMembers)
				{
					if (member is IMethodSymbol method)
					{
						var attributes = method.GetAttributes();
						if (!attributes.Any(a => a.AttributeClass?.ContainingNamespace.Name == "Kyber")) continue;
						if (attributes.Any(a => a.AttributeClass?.Name == "UpdateAttribute")) updateCalls.Add(new LifecycleMethodCall(systemClass, method.Name, 0));
						if (attributes.Any(a => a.AttributeClass?.Name == "DrawAttribute")) drawCalls.Add(new LifecycleMethodCall(systemClass, method.Name, 0));
					}
				}
			}

			// Create an EnumToGenerate for use in the generation phase
			scenesToGenerate.Add(new SceneClass(sceneClassNamespace, sceneClassName, systems, updateCalls, drawCalls));
		}

		return scenesToGenerate;
	}
}