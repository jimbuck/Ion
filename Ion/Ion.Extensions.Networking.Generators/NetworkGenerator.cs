using System.Collections.Generic;
using System.Linq;
using System.Text;

using Ion.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Ion.Extensions.Networking.Generators;

/// <summary>
/// The networking generator: for every <c>[Replicated]</c> component, <c>[assembly: ReplicateComponent]</c> type and
/// <c>[NetworkMessage]</c> struct of the compilation it writes a <c>NetSerializer&lt;T&gt;</c> (full, delta, comparison and,
/// for interpolated components, a blend) and registers it with the process registry from a module initializer
/// (<c>IonNetworking.g.cs</c>). It reports ION201 to ION210.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class NetworkGenerator : IIncrementalGenerator
{
	internal const string FileName = "IonNetworking.g.cs";

	private const string Ns = "Ion.Extensions.Networking";

	/// <inheritdoc/>
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		context.RegisterSourceOutput(context.CompilationProvider, static (spc, compilation) => Execute(spc, compilation));
	}

	private static void Execute(SourceProductionContext context, Compilation compilation)
	{
		var replicatedAttribute = compilation.GetTypeByMetadataName(Ns + ".ReplicatedAttribute");
		if (replicatedAttribute is null) return;

		var predictedAttribute = compilation.GetTypeByMetadataName(Ns + ".PredictedAttribute");
		var interpolatedAttribute = compilation.GetTypeByMetadataName(Ns + ".InterpolatedAttribute");
		var messageAttribute = compilation.GetTypeByMetadataName(Ns + ".NetworkMessageAttribute");
		var replicateComponentAttribute = compilation.GetTypeByMetadataName(Ns + ".ReplicateComponentAttribute");
		var typesAssemblyAttribute = compilation.GetTypeByMetadataName(Ns + ".NetworkTypesAssemblyAttribute");

		var diagnostics = new List<Diagnostic>();
		var analyzer = new StructAnalyzer(compilation, diagnostics);
		var types = new List<NetworkTypeModel>();
		var sourceComponents = new List<(INamedTypeSymbol Type, Location Location)>();
		var sourceMessages = new List<(INamedTypeSymbol Type, Location Location)>();

		// [Replicated], [Predicted], [Interpolated] and [NetworkMessage] structs declared in the compilation.
		foreach (var type in DeclaredStructs(compilation))
		{
			var attributes = type.GetAttributes();
			var replicated = attributes.FirstOrDefault(a => Is(a, replicatedAttribute));
			var message = attributes.FirstOrDefault(a => Is(a, messageAttribute));
			var predicted = attributes.Any(a => Is(a, predictedAttribute));
			var interpolated = attributes.Any(a => Is(a, interpolatedAttribute));
			var location = type.Locations.FirstOrDefault() ?? Location.None;

			if (replicated is null && (predicted || interpolated))
			{
				diagnostics.Add(Diagnostic.Create(NetworkDiagnostics.MissingReplicated, location, type.ToDisplayString(), predicted ? "Predicted" : "Interpolated"));
			}

			if (replicated is not null)
			{
				var authority = Named(replicated, "Authority", 0);
				var model = Component(type, location, authority, predicted, interpolated, analyzer, diagnostics);
				if (model is not null) types.Add(model);
				sourceComponents.Add((type, location));
			}

			if (message is not null)
			{
				if (!type.IsUnmanagedType)
				{
					diagnostics.Add(Diagnostic.Create(NetworkDiagnostics.NotUnmanaged, location, type.ToDisplayString(), "network message"));
				}
				else if (analyzer.Analyze(type, location) is { } structModel)
				{
					types.Add(new NetworkTypeModel(structModel, Name(type), isMessage: true)
					{
						Delivery = Named(message, "Delivery", 3),
						Direction = Named(message, "Direction", 0),
					});
				}

				sourceMessages.Add((type, location));
			}
		}

		// [assembly: ReplicateComponent(typeof(T), ...)]
		if (replicateComponentAttribute is not null)
		{
			foreach (var attribute in compilation.Assembly.GetAttributes().Where(a => Is(a, replicateComponentAttribute)))
			{
				if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol type) continue;
				var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None;
				if (types.Any(t => !t.IsMessage && t.Name == Name(type))) continue;
				var model = Component(type, location, Named(attribute, "Authority", 0), NamedBool(attribute, "Predicted"), NamedBool(attribute, "Interpolated"), analyzer, diagnostics);
				if (model is not null) types.Add(model);
			}
		}

		// Registrations of referenced assemblies, called from this one so every type is known at startup.
		var references = new List<string>();
		if (typesAssemblyAttribute is not null)
		{
			foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
			{
				foreach (var attribute in assembly.GetAttributes().Where(a => Is(a, typesAssemblyAttribute)))
				{
					if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is INamedTypeSymbol registration
						&& compilation.IsSymbolAccessibleWithin(registration, compilation.Assembly))
					{
						references.Add(registration.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
					}
				}
			}
		}

		UsageChecks(compilation, sourceComponents, sourceMessages, diagnostics);

		foreach (var diagnostic in diagnostics) context.ReportDiagnostic(diagnostic);

		if (types.Count == 0 && references.Count == 0) return;
		context.AddSource(FileName, SourceText.From(Emit(compilation, types, analyzer.Nested.ToList(), references), Encoding.UTF8));
	}

	private static NetworkTypeModel? Component(INamedTypeSymbol type, Location location, int authority, bool predicted, bool interpolated, StructAnalyzer analyzer, List<Diagnostic> diagnostics)
	{
		var display = type.ToDisplayString();
		if (!type.IsUnmanagedType)
		{
			diagnostics.Add(Diagnostic.Create(NetworkDiagnostics.NotUnmanaged, location, display, "replicated component"));
			return null;
		}

		if (predicted && authority != 1)
		{
			diagnostics.Add(Diagnostic.Create(NetworkDiagnostics.PredictedWithoutOwner, location, display));
		}

		var model = analyzer.Analyze(type, location);
		if (model is null) return null;

		var size = model.MaxSize + 10;
		if (size > StructAnalyzer.MaxComponentBytes)
		{
			diagnostics.Add(Diagnostic.Create(NetworkDiagnostics.TooLarge, location, display, size, StructAnalyzer.MaxComponentBytes));
			return null;
		}

		return new NetworkTypeModel(model, Name(type), isMessage: false)
		{
			Authority = authority,
			Predicted = predicted,
			Interpolated = interpolated,
		};
	}

	private static IEnumerable<INamedTypeSymbol> DeclaredStructs(Compilation compilation)
	{
		var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
		foreach (var tree in compilation.SyntaxTrees)
		{
			SemanticModel? model = null;
			foreach (var node in tree.GetRoot().DescendantNodes(static n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax))
			{
				if (node is not TypeDeclarationSyntax declaration || declaration.AttributeLists.Count == 0) continue;
				if (declaration is not (StructDeclarationSyntax or RecordDeclarationSyntax)) continue;
				model ??= compilation.GetSemanticModel(tree);
				if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol { TypeKind: TypeKind.Struct } symbol && seen.Add(symbol)) yield return symbol;
			}
		}
	}

	// ION204 (replicated but not used as a component), ION206 (reader in a stage method), ION207/ION208 (messages sent or
	// read only).
	private static void UsageChecks(Compilation compilation, List<(INamedTypeSymbol Type, Location Location)> components, List<(INamedTypeSymbol Type, Location Location)> messages, List<Diagnostic> diagnostics)
	{
		var sends = compilation.GetTypeByMetadataName(Ns + ".SendsNetworkMessageAttribute");
		var reads = compilation.GetTypeByMetadataName(Ns + ".ReadsNetworkMessageAttribute");
		var stageAttribute = compilation.GetTypeByMetadataName("Ion.StageAttribute");
		var scopeAttribute = compilation.GetTypeByMetadataName("Ion.ScopeAttribute");
		var commands = compilation.GetTypeByMetadataName("Ion.Extensions.Ecs.Commands");

		var used = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
		var sent = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
		var read = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
		var checkComponents = components.Count > 0;

		foreach (var tree in compilation.SyntaxTrees)
		{
			SemanticModel? model = null;
			foreach (var node in tree.GetRoot().DescendantNodes())
			{
				switch (node)
				{
					case InvocationExpressionSyntax invocation:
					{
						model ??= compilation.GetSemanticModel(tree);
						if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method) break;

						if (checkComponents && IsEcsMethod(method, commands))
						{
							foreach (var argument in method.TypeArguments) used.Add(argument);
							foreach (var argument in method.ContainingType.TypeArguments) used.Add(argument);
						}

						var original = method.ReducedFrom ?? method.OriginalDefinition;
						foreach (var attribute in original.GetAttributes())
						{
							var isSend = Is(attribute, sends);
							var isRead = Is(attribute, reads);
							if (!isSend && !isRead) continue;
							var index = attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int i ? i : 0;
							if (index < 0 || index >= method.TypeArguments.Length) continue;
							var messageType = method.TypeArguments[index];
							if (isSend) sent.Add(messageType);
							if (isRead)
							{
								read.Add(messageType);
								if (method.Name == "Reader" && InStageMethod(invocation, model, stageAttribute, scopeAttribute) is { } stageMethod)
								{
									diagnostics.Add(Diagnostic.Create(NetworkDiagnostics.ReaderInStage, invocation.GetLocation(), messageType.ToDisplayString(), stageMethod));
								}
							}
						}

						break;
					}

					case MethodDeclarationSyntax method when checkComponents && method.AttributeLists.Count > 0:
					{
						model ??= compilation.GetSemanticModel(tree);
						if (model.GetDeclaredSymbol(method) is not IMethodSymbol symbol) break;
						if (!symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "QueryAttribute")) break;
						foreach (var parameter in symbol.Parameters) used.Add(parameter.Type);
						break;
					}

					case AttributeSyntax attribute when checkComponents && attribute.Name is GenericNameSyntax or QualifiedNameSyntax { Right: GenericNameSyntax }:
					{
						model ??= compilation.GetSemanticModel(tree);
						if (model.GetSymbolInfo(attribute).Symbol is IMethodSymbol { ContainingType: { } attributeType })
						{
							foreach (var argument in attributeType.TypeArguments) used.Add(argument);
						}

						break;
					}
				}
			}
		}

		foreach (var (type, location) in components)
		{
			if (!used.Contains(type)) diagnostics.Add(Diagnostic.Create(NetworkDiagnostics.NotUsedAsComponent, location, type.ToDisplayString()));
		}

		// Sent-only and read-only messages only make sense to report where the whole game is visible: an executable.
		if (compilation.Options.OutputKind is not (OutputKind.ConsoleApplication or OutputKind.WindowsApplication)) return;
		foreach (var (type, location) in messages)
		{
			var isSent = sent.Contains(type);
			var isRead = read.Contains(type);
			if (isSent && !isRead) diagnostics.Add(Diagnostic.Create(NetworkDiagnostics.SentNeverRead, location, type.ToDisplayString()));
			else if (isRead && !isSent) diagnostics.Add(Diagnostic.Create(NetworkDiagnostics.ReadNeverSent, location, type.ToDisplayString()));
		}
	}

	private static bool IsEcsMethod(IMethodSymbol method, INamedTypeSymbol? commands)
	{
		if (commands is not null && SymbolEqualityComparer.Default.Equals(method.ContainingType, commands)) return true;
		var ns = method.ContainingNamespace?.ToDisplayString() ?? "";
		return ns == "Arch.Core" || ns.StartsWith("Arch.Core.", System.StringComparison.Ordinal) || ns == "Arch.Buffer";
	}

	private static string? InStageMethod(SyntaxNode node, SemanticModel model, INamedTypeSymbol? stage, INamedTypeSymbol? scope)
	{
		var declaration = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
		if (declaration is null || declaration.AttributeLists.Count == 0) return null;
		if (model.GetDeclaredSymbol(declaration) is not IMethodSymbol method) return null;
		foreach (var attribute in method.GetAttributes())
		{
			for (var type = attribute.AttributeClass; type is not null; type = type.BaseType)
			{
				if (SymbolEqualityComparer.Default.Equals(type, stage) || SymbolEqualityComparer.Default.Equals(type, scope)) return method.Name;
			}
		}

		return null;
	}

	private static bool Is(AttributeData attribute, INamedTypeSymbol? type) =>
		type is not null && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, type);

	private static int Named(AttributeData attribute, string name, int fallback)
	{
		foreach (var argument in attribute.NamedArguments)
		{
			if (argument.Key == name && argument.Value.Value is { } value) return System.Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
		}

		return fallback;
	}

	private static bool NamedBool(AttributeData attribute, string name)
	{
		foreach (var argument in attribute.NamedArguments)
		{
			if (argument.Key == name && argument.Value.Value is bool value) return value;
		}

		return false;
	}

	private static string Name(INamedTypeSymbol type) => type.ToDisplayString();

	// Emission ----------------------------------------------------------------------------------------------------------

	private static string Emit(Compilation compilation, List<NetworkTypeModel> types, List<StructModel> nested, List<string> references)
	{
		var className = "NetworkTypes_" + Sanitize(compilation.AssemblyName ?? "Assembly");
		var w = new SourceWriter();
		w.WriteLine("// <auto-generated/>");
		w.WriteLine("#nullable enable");
		w.WriteLine("#pragma warning disable CS0618, CS1591, CA2255");
		w.WriteLine("");
		if (types.Count > 0) w.WriteLine($"[assembly: global::Ion.Extensions.Networking.NetworkTypesAssemblyAttribute(typeof(global::Ion.Generated.Networking.{className}))]");
		w.WriteLine("");
		w.WriteLine("namespace Ion.Generated.Networking");
		w.OpenBlock();
		w.WriteLine("/// <summary>The network types of this assembly (generated by Ion.Extensions.Networking.Generators).</summary>");
		w.WriteLine("[global::System.CodeDom.Compiler.GeneratedCode(\"Ion.Extensions.Networking.Generators\", \"1.0\")]");
		w.WriteLine("[global::System.Diagnostics.DebuggerNonUserCode]");
		w.WriteLine("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
		w.WriteLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
		w.WriteLine($"public static class {className}");
		w.OpenBlock();
		w.WriteLine("private static bool _registered;");
		w.WriteLine("");
		w.WriteLine("[global::System.Runtime.CompilerServices.ModuleInitializer]");
		w.WriteLine("internal static void Initialize() => Register();");
		w.WriteLine("");
		w.WriteLine("/// <summary>Registers this assembly's replicated components and network messages, and those of its references.</summary>");
		w.WriteLine("public static void Register()");
		w.OpenBlock();
		w.WriteLine("if (_registered) return;");
		w.WriteLine("_registered = true;");
		foreach (var reference in references.Distinct()) w.WriteLine($"{reference}.Register();");
		foreach (var type in types)
		{
			var t = type.Model.TypeName;
			var serializer = "new S_" + type.Model.Key + "()";
			if (type.IsMessage)
			{
				w.WriteLine($"global::Ion.Extensions.Networking.NetworkRegistry.Register(new global::Ion.Extensions.Networking.MessageTypeInfo<{t}>({Literal(type.Name)}, {Literal(type.Model.Layout)}, {serializer}, (global::Ion.Extensions.Networking.Delivery){type.Delivery}, (global::Ion.Extensions.Networking.MessageDirection){type.Direction}));");
			}
			else
			{
				w.WriteLine($"global::Ion.Extensions.Ecs.EcsComponents.Register<{t}>();");
				w.WriteLine($"global::Ion.Extensions.Networking.NetworkRegistry.Register(new global::Ion.Extensions.Networking.ReplicatedTypeInfo<{t}>({Literal(type.Name)}, {Literal(type.Model.Layout)}, {serializer}, (global::Ion.Extensions.Networking.Authority){type.Authority}, {Bool(type.Predicted)}, {Bool(type.Interpolated)}));");
			}
		}

		w.CloseBlock();

		foreach (var type in types) EmitSerializer(w, type);
		foreach (var model in nested) EmitHelpers(w, model);

		w.CloseBlock();
		w.CloseBlock();
		return w.ToString();
	}

	private static void EmitSerializer(SourceWriter w, NetworkTypeModel type)
	{
		var model = type.Model;
		var t = model.TypeName;
		var members = model.Members;
		w.WriteLine("");
		w.WriteLine($"private sealed class S_{model.Key} : global::Ion.Extensions.Networking.NetSerializer<{t}>");
		w.OpenBlock();
		w.WriteLine($"public override int MaxSize => {model.MaxSize};");
		w.WriteLine("");

		w.WriteLine($"public override void Write(ref global::Ion.Extensions.Networking.NetWriter w, in {t} v)");
		w.OpenBlock();
		foreach (var m in members) w.WriteLine(WriteStatement(m, "v." + m.Name));
		w.CloseBlock();
		w.WriteLine("");

		w.WriteLine($"public override {t} Read(ref global::Ion.Extensions.Networking.NetReader r)");
		w.OpenBlock();
		for (var i = 0; i < members.Count; i++) w.WriteLine($"var m{i} = {ReadExpression(members[i])};");
		w.WriteLine($"return default({t}) with {{ {Assignments(members)} }};");
		w.CloseBlock();
		w.WriteLine("");

		w.WriteLine($"public override bool WriteDelta(ref global::Ion.Extensions.Networking.NetWriter w, in {t} b, in {t} v)");
		w.OpenBlock();
		w.WriteLine("ulong mask = 0;");
		for (var i = 0; i < members.Count; i++) w.WriteLine($"if (!({EqualExpression(members[i], "b." + members[i].Name, "v." + members[i].Name)})) mask |= 1UL << {i};");
		w.WriteLine("if (mask == 0) return false;");
		w.WriteLine("w.WriteVarUInt64(mask);");
		for (var i = 0; i < members.Count; i++) w.WriteLine($"if ((mask & (1UL << {i})) != 0) {WriteStatement(members[i], "v." + members[i].Name)}");
		w.WriteLine("return true;");
		w.CloseBlock();
		w.WriteLine("");

		w.WriteLine($"public override {t} ReadDelta(ref global::Ion.Extensions.Networking.NetReader r, in {t} b)");
		w.OpenBlock();
		w.WriteLine("var mask = r.ReadVarUInt64();");
		w.WriteLine(members.Count >= 64 ? "_ = mask;" : $"if ((mask >> {members.Count}) != 0) r.Fail();");
		for (var i = 0; i < members.Count; i++) w.WriteLine($"var m{i} = (mask & (1UL << {i})) != 0 ? {ReadExpression(members[i])} : b.{members[i].Name};");
		w.WriteLine($"return b with {{ {Assignments(members)} }};");
		w.CloseBlock();
		w.WriteLine("");

		var equal = members.Count == 0 ? "true" : string.Join(" && ", members.Select(m => EqualExpression(m, "a." + m.Name, "b." + m.Name)));
		w.WriteLine($"public override bool Equal(in {t} a, in {t} b) => {equal};");

		if (type.Interpolated)
		{
			w.WriteLine("");
			var blends = string.Join(", ", members.Select(m => $"{m.Name} = {LerpExpression(m, "a." + m.Name, "b." + m.Name)}"));
			w.WriteLine($"public override {t} Interpolate(in {t} a, in {t} b, float t) => b with {{ {blends} }};");
		}

		w.CloseBlock();
	}

	private static void EmitHelpers(SourceWriter w, StructModel model)
	{
		var t = model.TypeName;
		var members = model.Members;
		w.WriteLine("");
		w.WriteLine($"private static void W_{model.Key}(ref global::Ion.Extensions.Networking.NetWriter w, in {t} v)");
		w.OpenBlock();
		foreach (var m in members) w.WriteLine(WriteStatement(m, "v." + m.Name));
		w.CloseBlock();
		w.WriteLine("");
		w.WriteLine($"private static {t} R_{model.Key}(ref global::Ion.Extensions.Networking.NetReader r)");
		w.OpenBlock();
		for (var i = 0; i < members.Count; i++) w.WriteLine($"var m{i} = {ReadExpression(members[i])};");
		w.WriteLine($"return default({t}) with {{ {Assignments(members)} }};");
		w.CloseBlock();
		var equal = members.Count == 0 ? "true" : string.Join(" && ", members.Select(m => EqualExpression(m, "a." + m.Name, "b." + m.Name)));
		w.WriteLine($"private static bool E_{model.Key}(in {t} a, in {t} b) => {equal};");
		var blends = string.Join(", ", members.Select(m => $"{m.Name} = {LerpExpression(m, "a." + m.Name, "b." + m.Name)}"));
		w.WriteLine($"private static {t} L_{model.Key}(in {t} a, in {t} b, float t) => b with {{ {blends} }};");
	}

	private static string Assignments(List<MemberModel> members) =>
		string.Join(", ", members.Select((m, i) => $"{m.Name} = m{i}"));

	private static string WriteStatement(MemberModel m, string value) => m.Kind switch
	{
		LeafKind.Enum => $"w.Write{Primitive(m.Underlying)}(({PrimitiveType(m.Underlying)}){value});",
		LeafKind.Struct => $"W_{m.Nested!.Key}(ref w, {value});",
		LeafKind.FixedString32 or LeafKind.FixedString64 or LeafKind.FixedString128 => $"w.Write{m.Kind}({value});",
		_ => $"w.Write{Primitive(m.Kind)}({value});",
	};

	private static string ReadExpression(MemberModel m) => m.Kind switch
	{
		LeafKind.Enum => $"({m.TypeName})r.Read{Primitive(m.Underlying)}()",
		LeafKind.Struct => $"R_{m.Nested!.Key}(ref r)",
		LeafKind.FixedString32 or LeafKind.FixedString64 or LeafKind.FixedString128 => $"r.Read{m.Kind}()",
		_ => $"r.Read{Primitive(m.Kind)}()",
	};

	private static string EqualExpression(MemberModel m, string a, string b) => m.Kind switch
	{
		LeafKind.Single or LeafKind.Double or LeafKind.Vector2 or LeafKind.Vector3 or LeafKind.Vector4 or LeafKind.Quaternion => $"global::Ion.Extensions.Networking.NetCompare.Same({a}, {b})",
		LeafKind.FixedString32 or LeafKind.FixedString64 or LeafKind.FixedString128 => $"{a}.Equals({b})",
		LeafKind.Struct => $"E_{m.Nested!.Key}({a}, {b})",
		_ => $"{a} == {b}",
	};

	private static string LerpExpression(MemberModel m, string a, string b) => m.Kind switch
	{
		LeafKind.Single or LeafKind.Double or LeafKind.Vector2 or LeafKind.Vector3 or LeafKind.Vector4 or LeafKind.Quaternion => $"global::Ion.Extensions.Networking.NetLerp.Lerp({a}, {b}, t)",
		LeafKind.Struct => $"L_{m.Nested!.Key}({a}, {b}, t)",
		_ => $"(t < 0.5f ? {a} : {b})",
	};

	private static string Primitive(LeafKind kind) => kind switch
	{
		LeafKind.Bool => "Bool",
		LeafKind.Byte => "Byte",
		LeafKind.SByte => "SByte",
		LeafKind.Int16 => "Int16",
		LeafKind.UInt16 => "UInt16",
		LeafKind.Int32 => "Int32",
		LeafKind.UInt32 => "UInt32",
		LeafKind.Int64 => "Int64",
		LeafKind.UInt64 => "UInt64",
		LeafKind.Char => "Char",
		LeafKind.Single => "Single",
		LeafKind.Double => "Double",
		LeafKind.Vector2 => "Vector2",
		LeafKind.Vector3 => "Vector3",
		LeafKind.Vector4 => "Vector4",
		LeafKind.Quaternion => "Quaternion",
		LeafKind.NetworkId => "NetworkId",
		_ => kind.ToString(),
	};

	private static string PrimitiveType(LeafKind kind) => kind switch
	{
		LeafKind.Bool => "bool",
		LeafKind.Byte => "byte",
		LeafKind.SByte => "sbyte",
		LeafKind.Int16 => "short",
		LeafKind.UInt16 => "ushort",
		LeafKind.Int32 => "int",
		LeafKind.UInt32 => "uint",
		LeafKind.Int64 => "long",
		LeafKind.UInt64 => "ulong",
		LeafKind.Char => "char",
		_ => "int",
	};

	private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

	private static string Bool(bool value) => value ? "true" : "false";

	private static string Sanitize(string name)
	{
		var builder = new StringBuilder(name.Length);
		foreach (var c in name) builder.Append(char.IsLetterOrDigit(c) ? c : '_');
		return builder.ToString();
	}
}
