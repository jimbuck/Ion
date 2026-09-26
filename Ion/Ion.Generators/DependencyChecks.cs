using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Ion.Generators;

/// <summary>
/// The service checks the runtime makes when it builds the root schedule (ION006, ION008, ION009), made at compile time
/// where they are certain: for types declared in this compilation that no registration call of this compilation mentions.
/// </summary>
/// <remarks>
/// A type counts as registered when it appears anywhere in the compilation in a <c>typeof</c>, or as a type argument of
/// a call on an <c>IServiceCollection</c> (<c>AddSingleton&lt;T&gt;()</c>, <c>TryAddScoped&lt;TService, TImpl&gt;()</c>,
/// <c>AddSingleton(sp =&gt; new T())</c>, custom <c>services.AddMyThings&lt;T&gt;()</c> helpers...). Registrations made
/// by assembly scanning or in another assembly are invisible, so only root schedule registrations of types declared here
/// are checked; suppress the diagnostic (<c>dotnet_diagnostic.ION009.severity = none</c>) if that is how a project
/// registers its systems. The runtime check still applies.
/// </remarks>
internal sealed class DependencyChecks(KnownSymbols known, RegistrationAnalyzer analyzer, Action<DiagnosticDescriptor, Location?, string> report)
{
	private readonly HashSet<ITypeSymbol> _mentioned = new(SymbolEqualityComparer.Default);
	private readonly HashSet<ITypeSymbol> _scoped = new(SymbolEqualityComparer.Default);
	private readonly HashSet<ITypeSymbol> _otherLifetime = new(SymbolEqualityComparer.Default);

	public void Run(CancellationToken cancellationToken)
	{
		if (known.ServiceCollection is null) return;

		Collect(cancellationToken);

		foreach (var call in analyzer.Calls)
		{
			var receiver = call.Method.Parameters.Length > 0 ? call.Method.Parameters[0].Type : null;
			if (!KnownSymbols.Is(receiver, known.IonApplicationInterface)) continue;

			var location = call.Invocation.GetLocation();
			switch (call.Kind)
			{
				case CallKind.UseSystem:
				{
					var system = call.System!;
					var needsInstance = system.Steps.Any(s => !s.Method.IsStatic || s.EndMethod is { IsStatic: false });
					if (needsInstance && IsLocal(system.Service))
					{
						if (!_mentioned.Contains(system.Service))
						{
							report(Diagnostics.UnregisteredSystem, location, $"System '{system.Service.MetadataName}' is not registered in the service collection. Register it (for example services.AddSingleton<{system.Service.MetadataName}>()) before building.");
						}
						else if (_scoped.Contains(system.Service) && !_otherLifetime.Contains(system.Service))
						{
							report(Diagnostics.ScopedServiceInRoot, location, $"System '{system.Service.MetadataName}' is registered as scoped but is used by the root schedule, which resolves from the root provider. Register it as a singleton, or use it inside a scene (UseScene).");
						}
					}

					foreach (var step in system.Steps.Where(s => s.Kind != ItemKind.Middleware))
					{
						CheckParameters(system.Name + "." + step.Method.Name, step.Method, step.Signature, location, system);
						if (step.EndMethod is { } end) CheckParameters(system.Name + "." + step.Method.Name, end, step.EndSignature, location, system, system.Name + "." + end.Name);
					}

					break;
				}

				case CallKind.Function:
					foreach (var service in call.Services) CheckService(call.Name ?? "function", service, location);
					break;
			}
		}
	}

	private void CheckParameters(string owner, IMethodSymbol method, SignatureKind signature, Location location, SystemInfo system, string? endOwner = null)
	{
		if (signature != SignatureKind.Injected) return;
		foreach (var parameter in method.Parameters)
		{
			if (KnownSymbols.Is(parameter.Type, known.GameTime)) continue;
			CheckService(endOwner ?? owner, parameter.Type, location);
		}
	}

	private void CheckService(string owner, ITypeSymbol service, Location location)
	{
		if (KnownSymbols.Is(service, known.ServiceProvider) || !IsLocal(service)) return;

		if (!_mentioned.Contains(service))
		{
			report(Diagnostics.UnresolvableParameter, location, $"'{owner}' injects '{SystemAnalyzer.RuntimeName(service)}', which is not registered in the service collection.");
		}
		else if (_scoped.Contains(service) && !_otherLifetime.Contains(service))
		{
			report(Diagnostics.ScopedServiceInRoot, location, $"'{owner}' injects scoped service '{SystemAnalyzer.RuntimeName(service)}' into a step of the root schedule, which resolves from the root provider. Inject it in a scene system, or register it as a singleton.");
		}
	}

	private bool IsLocal(ITypeSymbol type) =>
		type is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, known.Compilation.Assembly) && named.TypeKind != TypeKind.Error;

	private void Collect(CancellationToken cancellationToken)
	{
		foreach (var tree in known.Compilation.SyntaxTrees)
		{
			var model = analyzer.Model(tree);
			foreach (var node in tree.GetRoot(cancellationToken).DescendantNodes())
			{
				switch (node)
				{
					case TypeOfExpressionSyntax typeOf:
						if (model.GetTypeInfo(typeOf.Type, cancellationToken).Type is { } type) Add(type, null);
						break;

					case InvocationExpressionSyntax invocation when model.GetOperation(invocation, cancellationToken) is IInvocationOperation operation:
						var method = operation.TargetMethod;
						var onServices = (operation.Instance is { } instance && KnownSymbols.Is(instance.Type, known.ServiceCollection))
							|| (method.IsExtensionMethod && method.Parameters.Length > 0 && KnownSymbols.Is(method.Parameters[0].Type, known.ServiceCollection))
							|| method.ContainingType?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.ServiceDescriptor";
						if (!onServices) break;

						bool? scoped = method.Name.Contains("Scoped") ? true : method.Name.Contains("Singleton") || method.Name.Contains("Transient") ? false : null;
						foreach (var argument in method.TypeArguments) Add(argument, scoped);
						break;
				}
			}
		}
	}

	private void Add(ITypeSymbol type, bool? scoped)
	{
		_mentioned.Add(type);
		if (scoped is true) _scoped.Add(type);
		else _otherLifetime.Add(type);
	}
}
