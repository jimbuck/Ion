using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace Ion;

public static class UseSystemExtensions
{
	// We're going to keep all public constructors and public methods on middleware
	private const DynamicallyAccessedMemberTypes MiddlewareAccessibility = DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods;

	/// <summary>
	/// Adds a middleware type to the application's request pipeline.
	/// </summary>
	/// <typeparam name="TMiddleware">The middleware type.</typeparam>
	/// <param name="app">The <see cref="IApplicationBuilder"/> instance.</param>
	/// <param name="args">The arguments to pass to the middleware type instance's constructor.</param>
	/// <returns>The <see cref="IApplicationBuilder"/> instance.</returns>
	public static IIonApplication UseSystem(this IIonApplication app, Type middlewareType)
	{
		return UseSystem(app, middlewareType, middlewareType);
	}

	/// <summary>
	/// Adds a middleware type to the application's request pipeline.
	/// </summary>
	/// <typeparam name="TMiddleware">The middleware type.</typeparam>
	/// <param name="app">The <see cref="IApplicationBuilder"/> instance.</param>
	/// <param name="args">The arguments to pass to the middleware type instance's constructor.</param>
	/// <returns>The <see cref="IApplicationBuilder"/> instance.</returns>
	public static IIonApplication UseSystem<[DynamicallyAccessedMembers(MiddlewareAccessibility)] TMiddleware>(this IIonApplication app)
	{
		return UseSystem(app, typeof(TMiddleware), typeof(TMiddleware));
	}

	public static IIonApplication UseSystem<TService, [DynamicallyAccessedMembers(MiddlewareAccessibility)] TImplementation>(this IIonApplication app)
	{
		return UseSystem(app, typeof(TService), typeof(TImplementation));
	}

	/// <summary>
	/// Adds a middleware type to the application's request pipeline.
	/// </summary>
	/// <param name="app">The <see cref="IApplicationBuilder"/> instance.</param>
	/// <param name="implementationType">The middleware type.</param>
	/// <param name="args">The arguments to pass to the middleware type instance's constructor.</param>
	/// <returns>The <see cref="IApplicationBuilder"/> instance.</returns>
	public static IIonApplication UseSystem(this IIonApplication app, Type serviceType, [DynamicallyAccessedMembers(MiddlewareAccessibility)] Type implementationType)
	{
		var methods = implementationType.GetMethods(BindingFlags.Instance | BindingFlags.Public);

		foreach (var method in methods)
		{
			if (SystemMiddlewareBinder.TryGetSystemFunction<InitAttribute>(app.Services, method, serviceType, out var initMiddleware)) app.UseInit(initMiddleware);
			if (SystemMiddlewareBinder.TryGetSystemFunction<FirstAttribute>(app.Services, method, serviceType, out var firstMiddleware)) app.UseFirst(firstMiddleware);
			if (SystemMiddlewareBinder.TryGetSystemFunction<UpdateAttribute>(app.Services, method, serviceType, out var updateMiddleware)) app.UseUpdate(updateMiddleware);
			if (SystemMiddlewareBinder.TryGetSystemFunction<FixedUpdateAttribute>(app.Services, method, serviceType, out var fixedUpdateMiddleware)) app.UseFixedUpdate(fixedUpdateMiddleware);
			if (SystemMiddlewareBinder.TryGetSystemFunction<RenderAttribute>(app.Services, method, serviceType, out var renderMiddleware)) app.UseRender(renderMiddleware);
			if (SystemMiddlewareBinder.TryGetSystemFunction<LastAttribute>(app.Services, method, serviceType, out var lastMiddleware)) app.UseLast(lastMiddleware);
			if (SystemMiddlewareBinder.TryGetSystemFunction<DestroyAttribute>(app.Services, method, serviceType, out var destroyMiddleware)) app.UseDestroy(destroyMiddleware);
		}

		return app;
	}
}
