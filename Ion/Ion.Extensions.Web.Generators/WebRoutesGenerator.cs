using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ion.Extensions.Web.Generators;

/// <summary>
/// Emits the assembly's web route table: one <c>WebRoute</c> per <c>[Http]</c> attribute and one <c>WebSocketRoute</c>
/// per <c>[WebSocket]</c> method, each with a generated invoker that binds the parameters from the route, the query and
/// the body with typed parsers, calls the method directly and writes its result. The table is registered with
/// <c>WebRoutes</c> from a module initializer, so the server finds it without reflection.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class WebRoutesGenerator : IIncrementalGenerator
{
	internal const string HttpAttribute = "Ion.Extensions.Web.HttpAttribute";
	internal const string WebSocketAttribute = "Ion.Extensions.Web.WebSocketAttribute";
	private const string Web = "global::Ion.Extensions.Web.";

	/// <summary>The generated file's name.</summary>
	public const string FileName = "IonWebRoutes.g.cs";

	private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
		.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

	private static readonly SymbolDisplayFormat PlainTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var http = context.SyntaxProvider.ForAttributeWithMetadataName(HttpAttribute,
			static (node, _) => node is MethodDeclarationSyntax,
			static (ctx, ct) => AnalyzeHttp(ctx, ct));
		var sockets = context.SyntaxProvider.ForAttributeWithMetadataName(WebSocketAttribute,
			static (node, _) => node is MethodDeclarationSyntax,
			static (ctx, ct) => AnalyzeSocket(ctx, ct));
		var assemblyName = context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "Assembly");

		var all = http.Collect().Combine(sockets.Collect()).Combine(assemblyName);
		context.RegisterSourceOutput(all, static (spc, input) =>
		{
			var ((httpResults, socketResults), name) = input;
			Emit(spc, name, httpResults.AddRange(socketResults));
		});
	}

	// ---------------------------------------------------------------- analysis

	private static EndpointResult AnalyzeHttp(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
	{
		var method = (IMethodSymbol)ctx.TargetSymbol;
		var compilation = ctx.SemanticModel.Compilation;
		var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
		var routes = ImmutableArray.CreateBuilder<HttpRouteModel>();
		var methodLocation = LocationInfo.From(method.Locations.FirstOrDefault());

		if (!CheckMethod(method, "[Http]", diagnostics, methodLocation))
		{
			return new EndpointResult(new(routes.ToImmutable()), default, new(diagnostics.ToImmutable()));
		}

		foreach (var attribute in ctx.Attributes)
		{
			ct.ThrowIfCancellationRequested();
			var location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation()) ?? methodLocation;
			if (attribute.ConstructorArguments.Length != 2 || attribute.ConstructorArguments[0].Value is not string verb || attribute.ConstructorArguments[1].Value is not string template)
			{
				continue;
			}

			var upper = verb.ToUpperInvariant();
			if (upper is not ("GET" or "HEAD" or "POST" or "PUT" or "PATCH" or "DELETE"))
			{
				diagnostics.Add(new("ION401", location, $"'{verb}' on {Display(method)} is not a supported HTTP method (GET, HEAD, POST, PUT, PATCH or DELETE)."));
				continue;
			}

			if (!RouteTemplate.TryParse(template, out var segments, out var error))
			{
				diagnostics.Add(new("ION401", location, $"The route '{template}' of {Display(method)} is invalid: {error}."));
				continue;
			}

			var access = NamedInt(attribute, "Access");
			var json = NamedType(attribute, "Json") ?? FindJsonContext(method, compilation);
			var body = BuildHttpInvoker(method, segments, json, compilation, diagnostics, location, out var ok);
			if (!ok) continue;

			routes.Add(new HttpRouteModel(upper, template, upper + " " + RouteTemplate.Shape(segments), access,
				method.ContainingType.ToDisplayString(PlainTypeFormat), Display(method), body, location));
		}

		return new EndpointResult(new(routes.ToImmutable()), default, new(diagnostics.ToImmutable()));
	}

	private static EndpointResult AnalyzeSocket(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
	{
		var method = (IMethodSymbol)ctx.TargetSymbol;
		var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
		var sockets = ImmutableArray.CreateBuilder<SocketRouteModel>();
		var attribute = ctx.Attributes[0];
		var location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation()) ?? LocationInfo.From(method.Locations.FirstOrDefault());

		if (CheckMethod(method, "[WebSocket]", diagnostics, location) && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string path)
		{
			if (!RouteTemplate.IsValidSocketPath(path, out var error))
			{
				diagnostics.Add(new("ION401", location, $"The WebSocket path '{path}' of {Display(method)} is invalid: {error}."));
			}
			else if (!method.ReturnsVoid || method.Parameters.Length != 1 || !IsWebType(method.Parameters[0].Type, "WebSocketMessage") || method.Parameters[0].RefKind is not (RefKind.None or RefKind.In))
			{
				diagnostics.Add(new("ION403", location, $"{Display(method)} must be 'void {method.Name}(in WebSocketMessage message)' to handle a WebSocket endpoint."));
			}
			else
			{
				var modifier = method.Parameters[0].RefKind == RefKind.In ? "in " : "";
				var call = $"(({method.ContainingType.ToDisplayString(PlainTypeFormat)})target).{Escape(method.Name)}({modifier}message);";
				var trimmed = path.Length > 1 && path[path.Length - 1] == '/' ? path.Substring(0, path.Length - 1) : path;
				sockets.Add(new SocketRouteModel(trimmed, NamedInt(attribute, "Access"), method.ContainingType.ToDisplayString(PlainTypeFormat), Display(method), call, location));
			}
		}

		return new EndpointResult(default, new(sockets.ToImmutable()), new(diagnostics.ToImmutable()));
	}

	/// <summary>The checks common to both kinds: an instance method, not generic, accessible from generated code, synchronous.</summary>
	private static bool CheckMethod(IMethodSymbol method, string kind, ImmutableArray<DiagnosticInfo>.Builder diagnostics, LocationInfo? location)
	{
		if (method.IsStatic)
		{
			diagnostics.Add(new("ION403", location, $"{Display(method)} is static: {kind} methods are instance methods of a system (a registered service)."));
			return false;
		}

		if (method.IsGenericMethod)
		{
			diagnostics.Add(new("ION403", location, $"{Display(method)} is generic: {kind} methods cannot have type parameters."));
			return false;
		}

		for (var type = method.ContainingType; type is not null; type = type.ContainingType)
		{
			if (type.IsGenericType)
			{
				diagnostics.Add(new("ION403", location, $"{Display(method)} is declared in the generic type {type.Name}: {kind} methods must be in non-generic types."));
				return false;
			}

			if (type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
			{
				diagnostics.Add(new("ION403", location, $"{Display(method)} is declared in {type.Name}, which is not public or internal: the generated route table cannot call it."));
				return false;
			}
		}

		if (method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
		{
			diagnostics.Add(new("ION403", location, $"{Display(method)} is not public or internal: the generated route table cannot call it."));
			return false;
		}

		if (method.IsAsync || IsTaskLike(method.ReturnType))
		{
			diagnostics.Add(new("ION407", location, $"{Display(method)} is asynchronous: {kind} handlers run synchronously on the game thread at the end of a frame (no async in the frame). Return the result directly."));
			return false;
		}

		if (method.ReturnsByRef || method.ReturnsByRefReadonly)
		{
			diagnostics.Add(new("ION403", location, $"{Display(method)} returns by reference, which an endpoint cannot."));
			return false;
		}

		return true;
	}

	private static bool IsTaskLike(ITypeSymbol type)
	{
		var name = type.OriginalDefinition.ToDisplayString();
		return name is "System.Threading.Tasks.Task" or "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask" or "System.Threading.Tasks.ValueTask<TResult>"
			or "System.Collections.Generic.IAsyncEnumerable<T>";
	}

	private enum SimpleKind
	{
		None,
		String,
		Bool,
		Parsable,
	}

	/// <summary>Whether <paramref name="type"/> binds from text (route or query), and how.</summary>
	private static SimpleKind Simple(ITypeSymbol type, out ITypeSymbol underlying)
	{
		underlying = type;
		if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable) underlying = nullable.TypeArguments[0];
		switch (underlying.SpecialType)
		{
			case SpecialType.System_String:
				return ReferenceEquals(underlying, type) ? SimpleKind.String : SimpleKind.None;
			case SpecialType.System_Boolean:
				return SimpleKind.Bool;
			case SpecialType.System_Byte:
			case SpecialType.System_SByte:
			case SpecialType.System_Int16:
			case SpecialType.System_UInt16:
			case SpecialType.System_Int32:
			case SpecialType.System_UInt32:
			case SpecialType.System_Int64:
			case SpecialType.System_UInt64:
			case SpecialType.System_Single:
			case SpecialType.System_Double:
			case SpecialType.System_Decimal:
				return SimpleKind.Parsable;
		}

		return underlying.ToDisplayString() == "System.Guid" ? SimpleKind.Parsable : SimpleKind.None;
	}

	private static string BuildHttpInvoker(IMethodSymbol method, ImmutableArray<RouteSegment> segments, INamedTypeSymbol? json, Compilation compilation,
		ImmutableArray<DiagnosticInfo>.Builder diagnostics, LocationInfo? location, out bool ok)
	{
		var valid = true;
		var body = new StringBuilder();
		var args = new List<string>();
		var routeNames = segments.Where(static s => s.Kind != SegmentKind.Literal).Select(static s => s.Name).ToList();
		var usedRouteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var hasBody = false;
		var jsonTypes = new List<ITypeSymbol>();
		var indent = "\t\t\t";

		for (var i = 0; i < method.Parameters.Length; i++)
		{
			var p = method.Parameters[i];
			var local = "p" + i.ToString(CultureInfo.InvariantCulture);
			var type = p.Type;
			var typeName = type.ToDisplayString(TypeFormat);
			var where = $"parameter '{p.Name}' of {Display(method)}";

			if (IsWebType(type, "WebRequest"))
			{
				if (p.RefKind is not (RefKind.None or RefKind.In)) { Fail($"The {where} must be a WebRequest passed by value or 'in'."); continue; }
				args.Add(p.RefKind == RefKind.In ? "in request" : "request");
				continue;
			}

			if (IsWebType(type, "WebResponse"))
			{
				if (p.RefKind is not (RefKind.None or RefKind.Ref)) { Fail($"The {where} must be a WebResponse passed by value or 'ref'."); continue; }
				if (p.RefKind == RefKind.Ref)
				{
					body.Append(indent).Append("var ").Append(local).Append(" = response;\n");
					args.Add("ref " + local);
				}
				else
				{
					args.Add("response");
				}

				continue;
			}

			if (p.RefKind != RefKind.None) { Fail($"The {where} is passed by reference ('{p.RefKind.ToString().ToLowerInvariant()}'); only WebRequest ('in') and WebResponse ('ref') can be."); continue; }

			if (p.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString() == "Ion.Extensions.Web.FromBodyAttribute"))
			{
				if (hasBody) { Fail($"The {where} is a second [FromBody] parameter: a request has one body."); continue; }
				hasBody = true;
				if (type.SpecialType == SpecialType.System_String)
				{
					body.Append(indent).Append("var ").Append(local).Append(" = ").Append(Web).Append("WebBinding.BodyString(in request);\n");
					args.Add(local);
				}
				else if (IsByteSpan(type))
				{
					args.Add("request.Body");
				}
				else if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
				{
					body.Append(indent).Append("var ").Append(local).Append(" = request.Body.ToArray();\n");
					args.Add(local);
				}
				else if (type.IsRefLikeType || type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer || type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Interface)
				{
					diagnostics.Add(new("ION405", location, $"The body type {type.ToDisplayString()} of {Display(method)} cannot be read: use string, ReadOnlySpan<byte>, byte[] or a type of a JsonSerializerContext."));
					valid = false;
				}
				else if (json is null)
				{
					diagnostics.Add(new("ION405", location, $"The body type {type.ToDisplayString()} of {Display(method)} needs a JsonSerializerContext: set [Http(..., Json = typeof(MyJsonContext))], or put [WebJson(typeof(MyJsonContext))] on the class or the assembly."));
					valid = false;
				}
				else
				{
					var plain = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(PlainTypeFormat);
					jsonTypes.Add(type);
					body.Append(indent).Append("if (!").Append(Web).Append("WebBinding.TryReadJson<").Append(plain).Append(">(request.Body, ").Append(JsonInfo(json, plain)).Append(", out var ").Append(local).Append(", out var e").Append(i.ToString(CultureInfo.InvariantCulture)).Append("))\n")
						.Append(indent).Append("{\n")
						.Append(indent).Append("\tresponse.Error(400, e").Append(i.ToString(CultureInfo.InvariantCulture)).Append("!);\n")
						.Append(indent).Append("\treturn;\n")
						.Append(indent).Append("}\n\n");
					args.Add(type.NullableAnnotation == NullableAnnotation.Annotated || type.IsValueType ? local : local + "!");
				}

				continue;
			}

			var routeIndex = routeNames.FindIndex(n => string.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase));
			var kind = Simple(type, out var underlying);
			if (routeIndex >= 0)
			{
				usedRouteNames.Add(p.Name);
				var catchAll = segments.First(s => s.Kind != SegmentKind.Literal && string.Equals(s.Name, p.Name, StringComparison.OrdinalIgnoreCase)).Kind == SegmentKind.CatchAll;
				if (kind == SimpleKind.None || (catchAll && kind != SimpleKind.String))
				{
					Fail($"The route {where} is a {type.ToDisplayString()}: route values bind to string{(catchAll ? " (a catch-all is always a string)" : ", bool, a number type or Guid")}.");
					continue;
				}

				var raw = "request.RouteValue(" + routeIndex.ToString(CultureInfo.InvariantCulture) + ")";
				if (kind == SimpleKind.String)
				{
					body.Append(indent).Append("var ").Append(local).Append(" = ").Append(Web).Append("WebBinding.RouteString(").Append(raw).Append(");\n");
				}
				else
				{
					body.Append(indent).Append(typeName).Append(' ').Append(local).Append(";\n");
					AppendParse(body, indent, kind, underlying, raw, local, $"Invalid value for route parameter '{p.Name}'.");
				}

				args.Add(local);
				continue;
			}

			if (kind == SimpleKind.None)
			{
				Fail($"The {where} is a {type.ToDisplayString()}, which cannot come from the query string: query and route values bind to string, bool, a number type or Guid (use [FromBody] for a JSON body).");
				continue;
			}

			// A query parameter, by the parameter's name.
			var optional = p.HasExplicitDefaultValue || type.NullableAnnotation == NullableAnnotation.Annotated || !ReferenceEquals(underlying, type);
			var q = "q" + i.ToString(CultureInfo.InvariantCulture);
			body.Append(indent).Append(typeName).Append(' ').Append(local).Append(";\n");
			body.Append(indent).Append("if (request.TryGetQuery(").Append(Utf8Literal(p.Name)).Append(", out var ").Append(q).Append("))\n").Append(indent).Append("{\n");
			if (kind == SimpleKind.String)
			{
				body.Append(indent).Append('\t').Append(local).Append(" = ").Append(Web).Append("WebBinding.QueryString(").Append(q).Append(");\n");
			}
			else
			{
				AppendParse(body, indent + "\t", kind, underlying, q, local, $"Invalid value for query parameter '{p.Name}'.");
			}

			body.Append(indent).Append("}\n").Append(indent).Append("else\n").Append(indent).Append("{\n");
			if (optional)
			{
				body.Append(indent).Append('\t').Append(local).Append(" = ").Append(DefaultValue(p, typeName)).Append(";\n");
			}
			else
			{
				body.Append(indent).Append("\tresponse.Error(400, ").Append(SymbolDisplay.FormatLiteral($"Missing query parameter '{p.Name}'.", quote: true)).Append(");\n")
					.Append(indent).Append("\treturn;\n");
			}

			body.Append(indent).Append("}\n\n");
			args.Add(local);
		}

		foreach (var name in routeNames)
		{
			if (usedRouteNames.Contains(name)) continue;
			diagnostics.Add(new("ION401", location, $"The route parameter '{name}' has no parameter of that name in {Display(method)}."));
			valid = false;
		}

		// The call and the result.
		var call = $"(({method.ContainingType.ToDisplayString(PlainTypeFormat)})target).{Escape(method.Name)}({string.Join(", ", args)})";
		var result = method.ReturnType;
		if (method.ReturnsVoid)
		{
			body.Append(indent).Append(call).Append(";\n");
		}
		else
		{
			body.Append(indent).Append("var result = ").Append(call).Append(";\n");
			var nullableValue = result is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n ? n.TypeArguments[0] : null;
			var core = nullableValue ?? result;
			var write = WriteResult(core, nullableValue is null ? "result" : "value");
			if (result.SpecialType == SpecialType.System_String)
			{
				body.Append(indent).Append("if (result is not null) response.Text(result);\n");
			}
			else if (write is not null)
			{
				if (nullableValue is null) body.Append(indent).Append(write).Append(";\n");
				else body.Append(indent).Append("if (result is { } value) ").Append(write).Append(";\n").Append(indent).Append("else response.JsonNull();\n");
			}
			else if (result.IsRefLikeType || result.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer || result.SpecialType == SpecialType.System_Object || result.TypeKind == TypeKind.Interface)
			{
				diagnostics.Add(new("ION405", location, $"The result type {result.ToDisplayString()} of {Display(method)} cannot be written: return void, string, bool, a number, or a type of a JsonSerializerContext."));
				valid = false;
			}
			else if (json is null)
			{
				diagnostics.Add(new("ION405", location, $"The result type {result.ToDisplayString()} of {Display(method)} needs a JsonSerializerContext: set [Http(..., Json = typeof(MyJsonContext))], or put [WebJson(typeof(MyJsonContext))] on the class or the assembly."));
				valid = false;
			}
			else
			{
				var plain = result.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(PlainTypeFormat);
				jsonTypes.Add(result);
				body.Append(indent).Append("response.Json<").Append(plain).Append(">(result").Append(result.IsValueType ? "" : "!").Append(", ").Append(JsonInfo(json, plain)).Append(");\n");
			}
		}

		if (json is not null)
		{
			if (!InheritsFrom(json, "System.Text.Json.Serialization.JsonSerializerContext"))
			{
				diagnostics.Add(new("ION405", location, $"{json.ToDisplayString()} (the JSON context of {Display(method)}) is not a System.Text.Json JsonSerializerContext."));
				valid = false;
			}
			else
			{
				foreach (var type in jsonTypes)
				{
					if (!ContextHas(json, type))
					{
						diagnostics.Add(new("ION406", location, $"{json.Name} has no [JsonSerializable(typeof({type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString()}))], which {Display(method)} needs: the endpoint fails at run time until it is added."));
					}
				}
			}
		}

		ok = valid;
		return body.ToString();

		void Fail(string message)
		{
			diagnostics.Add(new("ION404", location, message));
			valid = false;
		}
	}

	private static void AppendParse(StringBuilder body, string indent, SimpleKind kind, ITypeSymbol underlying, string raw, string local, string error)
	{
		var name = underlying.ToDisplayString(PlainTypeFormat);
		var parse = kind == SimpleKind.Bool
			? $"{Web}WebBinding.TryParseBool({raw}, out var {local}v)"
			: $"{Web}WebBinding.TryParse<{name}>({raw}, out var {local}v)";
		body.Append(indent).Append("if (!").Append(parse).Append(")\n")
			.Append(indent).Append("{\n")
			.Append(indent).Append("\tresponse.Error(400, ").Append(SymbolDisplay.FormatLiteral(error, quote: true)).Append(");\n")
			.Append(indent).Append("\treturn;\n")
			.Append(indent).Append("}\n\n")
			.Append(indent).Append(local).Append(" = ").Append(local).Append("v;\n");
	}

	private static string? WriteResult(ITypeSymbol type, string value) => type.SpecialType switch
	{
		SpecialType.System_Boolean => $"response.JsonBool({value})",
		SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64 => $"response.JsonNumber((long){value})",
		SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64 => $"response.JsonNumber((ulong){value})",
		SpecialType.System_Single or SpecialType.System_Double => $"response.JsonNumber((double){value})",
		SpecialType.System_Decimal => $"response.JsonNumber({value})",
		_ => null,
	};

	private static string DefaultValue(IParameterSymbol p, string typeName)
	{
		if (!p.HasExplicitDefaultValue || p.ExplicitDefaultValue is null) return "default";
		var value = p.ExplicitDefaultValue;
		string literal = value switch
		{
			string s => SymbolDisplay.FormatLiteral(s, quote: true),
			bool b => b ? "true" : "false",
			double d when double.IsNaN(d) => "global::System.Double.NaN",
			double d when double.IsPositiveInfinity(d) => "global::System.Double.PositiveInfinity",
			double d when double.IsNegativeInfinity(d) => "global::System.Double.NegativeInfinity",
			float f when float.IsNaN(f) => "global::System.Single.NaN",
			float f when float.IsPositiveInfinity(f) => "global::System.Single.PositiveInfinity",
			float f when float.IsNegativeInfinity(f) => "global::System.Single.NegativeInfinity",
			float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
			double d => d.ToString("R", CultureInfo.InvariantCulture) + "d",
			decimal m => m.ToString(CultureInfo.InvariantCulture) + "m",
			_ => SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false),
		};
		return value is string ? literal : $"({typeName})({literal})";
	}

	private static string JsonInfo(INamedTypeSymbol context, string plainType)
	{
		var ctx = context.ToDisplayString(PlainTypeFormat);
		return $"(JsonOf<{ctx}, {plainType}>.Value ??= {Web}WebBinding.TypeInfo<{plainType}>({ctx}.Default))";
	}

	private static bool ContextHas(INamedTypeSymbol context, ITypeSymbol type)
	{
		var plain = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
		if (plain is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n) plain = n.TypeArguments[0];
		foreach (var attribute in context.GetAttributes())
		{
			if (attribute.AttributeClass?.ToDisplayString() != "System.Text.Json.Serialization.JsonSerializableAttribute") continue;
			if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is ITypeSymbol listed
				&& SymbolEqualityComparer.Default.Equals(listed.WithNullableAnnotation(NullableAnnotation.NotAnnotated), plain))
			{
				return true;
			}
		}

		return false;
	}

	private static INamedTypeSymbol? FindJsonContext(IMethodSymbol method, Compilation compilation)
	{
		for (var type = method.ContainingType; type is not null; type = type.ContainingType)
		{
			if (WebJson(type.GetAttributes()) is { } context) return context;
		}

		return WebJson(compilation.Assembly.GetAttributes());
	}

	private static INamedTypeSymbol? WebJson(ImmutableArray<AttributeData> attributes)
	{
		foreach (var attribute in attributes)
		{
			if (attribute.AttributeClass?.ToDisplayString() == "Ion.Extensions.Web.WebJsonAttribute" && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is INamedTypeSymbol context)
			{
				return context;
			}
		}

		return null;
	}

	private static bool InheritsFrom(INamedTypeSymbol type, string baseName)
	{
		for (var t = type.BaseType; t is not null; t = t.BaseType)
		{
			if (t.ToDisplayString() == baseName) return true;
		}

		return false;
	}

	private static bool IsWebType(ITypeSymbol type, string name) =>
		type.Name == name && type.ContainingNamespace?.ToDisplayString() == "Ion.Extensions.Web";

	private static bool IsByteSpan(ITypeSymbol type) =>
		type is INamedTypeSymbol { Name: "ReadOnlySpan", TypeArguments.Length: 1 } span && span.ContainingNamespace?.ToDisplayString() == "System" && span.TypeArguments[0].SpecialType == SpecialType.System_Byte;

	private static int NamedInt(AttributeData attribute, string name)
	{
		foreach (var pair in attribute.NamedArguments)
		{
			if (pair.Key == name && pair.Value.Value is int value) return value;
		}

		return 0;
	}

	private static INamedTypeSymbol? NamedType(AttributeData attribute, string name)
	{
		foreach (var pair in attribute.NamedArguments)
		{
			if (pair.Key == name && pair.Value.Value is INamedTypeSymbol type) return type;
		}

		return null;
	}

	private static string Display(IMethodSymbol method) => method.ContainingType.Name + "." + method.Name;

	private static string Escape(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

	private static string Utf8Literal(string text) => SymbolDisplay.FormatLiteral(text, quote: true) + "u8";

	// ---------------------------------------------------------------- emission

	private static void Emit(SourceProductionContext spc, string assemblyName, ImmutableArray<EndpointResult> results)
	{
		var routes = new List<HttpRouteModel>();
		var sockets = new List<SocketRouteModel>();
		foreach (var result in results)
		{
			foreach (var d in result.Diagnostics) spc.ReportDiagnostic(Diagnostic.Create(WebDiagnostics.ForId(d.Id), d.Location?.ToLocation(), d.Message));
			routes.AddRange(result.Routes);
			sockets.AddRange(result.Sockets);
		}

		// Duplicates: same method and path shape; WebSocket paths are compared case-insensitively.
		var shapes = new Dictionary<string, HttpRouteModel>(StringComparer.Ordinal);
		var keptRoutes = new List<HttpRouteModel>();
		foreach (var route in routes.OrderBy(static r => r.Location?.Path, StringComparer.Ordinal).ThenBy(static r => r.Location?.Span.Start ?? 0))
		{
			if (shapes.TryGetValue(route.ShapeKey, out var first))
			{
				spc.ReportDiagnostic(Diagnostic.Create(WebDiagnostics.DuplicateRoute, route.Location?.ToLocation(),
					$"{route.DisplayName} ({route.Verb} {route.Template}) matches the same requests as {first.DisplayName} ({first.Verb} {first.Template})."));
				continue;
			}

			shapes.Add(route.ShapeKey, route);
			keptRoutes.Add(route);
		}

		var paths = new Dictionary<string, SocketRouteModel>(StringComparer.OrdinalIgnoreCase);
		var keptSockets = new List<SocketRouteModel>();
		foreach (var socket in sockets.OrderBy(static s => s.Location?.Path, StringComparer.Ordinal).ThenBy(static s => s.Location?.Span.Start ?? 0))
		{
			if (paths.TryGetValue(socket.Path, out var first))
			{
				spc.ReportDiagnostic(Diagnostic.Create(WebDiagnostics.DuplicateRoute, socket.Location?.ToLocation(),
					$"{socket.DisplayName} uses the WebSocket path {socket.Path} of {first.DisplayName}."));
				continue;
			}

			paths.Add(socket.Path, socket);
			keptSockets.Add(socket);
		}

		if (keptRoutes.Count == 0 && keptSockets.Count == 0) return;

		var className = "IonWebRoutes_" + new string([.. assemblyName.Select(static c => char.IsLetterOrDigit(c) ? c : '_')]);
		var s = new StringBuilder();
		s.Append("// <auto-generated/>\n");
		s.Append("// The web route table of this assembly, generated by Ion.Extensions.Web.Generators from its [Http] and [WebSocket] methods.\n");
		s.Append("#nullable enable\n");
		s.Append("#pragma warning disable CS0612, CS0618, CA2255\n\n");
		s.Append("namespace Ion.Generated\n{\n");
		s.Append("\t[global::System.CodeDom.Compiler.GeneratedCode(\"Ion.Extensions.Web.Generators\", \"1.0\")]\n");
		s.Append("\t[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]\n");
		s.Append("\tinternal static class ").Append(className).Append("\n\t{\n");
		s.Append("\t\t/// <summary>The route table: registered before any code of the assembly runs.</summary>\n");
		s.Append("\t\tpublic static readonly ").Append(Web).Append("WebRouteTable Table = new(").Append(SymbolDisplay.FormatLiteral(assemblyName, quote: true)).Append(",\n");
		s.Append("\t\t\tnew ").Append(Web).Append("WebRoute[]\n\t\t\t{\n");
		for (var i = 0; i < keptRoutes.Count; i++)
		{
			var r = keptRoutes[i];
			s.Append("\t\t\t\tnew(").Append(SymbolDisplay.FormatLiteral(r.Verb, quote: true)).Append(", ").Append(SymbolDisplay.FormatLiteral(r.Template, quote: true))
				.Append(", typeof(").Append(r.TypeName).Append("), static sp => sp.GetService(typeof(").Append(r.TypeName).Append(")), Route").Append(i.ToString(CultureInfo.InvariantCulture))
				.Append(", (").Append(Web).Append("WebAccess)").Append(r.Access.ToString(CultureInfo.InvariantCulture)).Append(", ").Append(SymbolDisplay.FormatLiteral(r.DisplayName, quote: true)).Append("),\n");
		}

		s.Append("\t\t\t},\n\t\t\tnew ").Append(Web).Append("WebSocketRoute[]\n\t\t\t{\n");
		for (var i = 0; i < keptSockets.Count; i++)
		{
			var w = keptSockets[i];
			s.Append("\t\t\t\tnew(").Append(SymbolDisplay.FormatLiteral(w.Path, quote: true))
				.Append(", typeof(").Append(w.TypeName).Append("), static sp => sp.GetService(typeof(").Append(w.TypeName).Append(")), Socket").Append(i.ToString(CultureInfo.InvariantCulture))
				.Append(", (").Append(Web).Append("WebAccess)").Append(w.Access.ToString(CultureInfo.InvariantCulture)).Append(", ").Append(SymbolDisplay.FormatLiteral(w.DisplayName, quote: true)).Append("),\n");
		}

		s.Append("\t\t\t});\n\n");
		s.Append("\t\t[global::System.Runtime.CompilerServices.ModuleInitializer]\n");
		s.Append("\t\tinternal static void Register() => ").Append(Web).Append("WebRoutes.Register(Table);\n");

		for (var i = 0; i < keptRoutes.Count; i++)
		{
			var r = keptRoutes[i];
			s.Append("\n\t\t// ").Append(r.Verb).Append(' ').Append(r.Template).Append(": ").Append(r.DisplayName).Append('\n');
			s.Append("\t\tprivate static void Route").Append(i.ToString(CultureInfo.InvariantCulture)).Append("(object target, in ").Append(Web).Append("WebRequest request, ").Append(Web).Append("WebResponse response)\n\t\t{\n");
			s.Append(r.Body);
			s.Append("\t\t}\n");
		}

		for (var i = 0; i < keptSockets.Count; i++)
		{
			var w = keptSockets[i];
			s.Append("\n\t\t// WebSocket ").Append(w.Path).Append(": ").Append(w.DisplayName).Append('\n');
			s.Append("\t\tprivate static void Socket").Append(i.ToString(CultureInfo.InvariantCulture)).Append("(object target, in ").Append(Web).Append("WebSocketMessage message) =>\n\t\t\t")
				.Append(w.Call).Append('\n');
		}

		s.Append("\n\t\t/// <summary>The JSON type information of a context, looked up once.</summary>\n");
		s.Append("\t\tprivate static class JsonOf<TContext, T>\n\t\t{\n\t\t\tpublic static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>? Value;\n\t\t}\n");
		s.Append("\t}\n}\n");
		spc.AddSource(FileName, s.ToString());
	}
}
