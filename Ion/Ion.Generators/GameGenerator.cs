using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Ion.Generators;

[Generator]
public class GameGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
#if DEBUGGENERATORS
		if (!Debugger.IsAttached)
		{
			Debugger.Launch();
		}
#endif

		Debug.WriteLine($"{nameof(GameGenerator)}.{nameof(Initialize)}");

		Debug.WriteLine($"{nameof(GameGenerator)}.CreateAttributes");
		// Add the marker attribute to the compilation
		context.RegisterPostInitializationOutput(ctx => ctx.AddSource("IonAttributes.g.cs", SourceText.From(SourceGenerationHelper.Attributes, Encoding.UTF8)));

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

				if (attributeContainingTypeSymbol.ContainingNamespace.Name == "Ion" && attributeContainingTypeSymbol.Name == "SystemAttribute") return classDeclarationSyntax;
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
		INamedTypeSymbol? systemAttribute = compilation.GetTypeByMetadataName("Ion.SystemAttribute`1");

		if (systemAttribute == null) return scenesToGenerate;

		foreach (var sceneClassDeclarationSyntax in classes)
		{
			ct.ThrowIfCancellationRequested();

			SemanticModel semanticModel = compilation.GetSemanticModel(sceneClassDeclarationSyntax.SyntaxTree);
			if (semanticModel.GetDeclaredSymbol(sceneClassDeclarationSyntax) is not INamedTypeSymbol sceneClassSymbol) continue;

			string sceneClassNamespace = sceneClassSymbol.ContainingNamespace.ToString();
			string sceneClassName = sceneClassSymbol.Name;

			var systemSymbols = sceneClassSymbol.GetAttributes()
				.Select(a => a.AttributeClass?.ToString() ?? string.Empty)
				.Where(ac => ac?.StartsWith("Ion.SystemAttribute<") ?? false)
				.Select(ac =>
				{
					// TODO: Move the getting of the generic type out into a helper method.
					var systemTypeName = Regex.Match(ac, @"^Ion\.SystemAttribute<(.*)>$").Groups[1].Value;
					if (systemTypeName is null) return null;
					return compilation.GetTypeByMetadataName(systemTypeName);
				}).ToArray() ?? new INamedTypeSymbol[0];

			var systems = new List<SystemClass>();
			var updateCalls = new List<LifecycleMethodCall>();
			var drawCalls = new List<LifecycleMethodCall>();

			foreach (var systemSymbol in systemSymbols)
			{
				if (systemSymbol is null) continue;

				var systemNamespace = systemSymbol.ContainingNamespace.ToString();
				var systemClassName = systemSymbol.Name;

				var systemClass = new SystemClass(systemNamespace, systemClassName, SourceGenerationHelper.ToPrivateName(systemClassName));
				systems.Add(systemClass);

				foreach (ISymbol member in systemSymbol.GetMembers())
				{
					if (member is IMethodSymbol method)
					{
						var methodAttributes = method.GetAttributes();
						if (!methodAttributes.Any(a => a.AttributeClass?.ContainingNamespace.Name == "Ion")) continue;
						// TODO: Check for UpdateDependsOnAttribute<T> and DrawDependsOnAttribute<T>.
						if (methodAttributes.Any(a => a.AttributeClass?.Name == "UpdateAttribute")) updateCalls.Add(new LifecycleMethodCall(systemClass, method.Name, int.MaxValue));
						if (methodAttributes.Any(a => a.AttributeClass?.Name == "DrawAttribute")) drawCalls.Add(new LifecycleMethodCall(systemClass, method.Name, int.MaxValue));
					}
				}
			}

			// Create an EnumToGenerate for use in the generation phase
			scenesToGenerate.Add(new SceneClass(sceneClassNamespace, sceneClassName, systems, updateCalls, drawCalls));
		}

		return scenesToGenerate;
	}
}