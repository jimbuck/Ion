﻿using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace Kyber.Builder;

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
	public static IKyberApplication UseSystem<[DynamicallyAccessedMembers(MiddlewareAccessibility)] TMiddleware>(this IKyberApplication app)
	{
		return UseSystem(app, typeof(TMiddleware), typeof(TMiddleware));
	}

	public static IKyberApplication UseSystem<TService, [DynamicallyAccessedMembers(MiddlewareAccessibility)] TImplementation>(this IKyberApplication app)
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
	public static IKyberApplication UseSystem(this IKyberApplication app, Type serviceType, [DynamicallyAccessedMembers(MiddlewareAccessibility)] Type implementationType)
	{
		var methods = implementationType.GetMethods(BindingFlags.Instance | BindingFlags.Public);

		foreach (var method in methods)
		{
			if (_tryGetSystemFunction<InitAttribute>(app, method, serviceType, out var initMiddleware)) app.UseInit(initMiddleware);
			if (_tryGetSystemFunction<FirstAttribute>(app, method, serviceType, out var firstMiddleware)) app.UseFirst(firstMiddleware);
			if (_tryGetSystemFunction<UpdateAttribute>(app, method, serviceType, out var updateMiddleware)) app.UseUpdate(updateMiddleware);
			if (_tryGetSystemFunction<FixedUpdateAttribute>(app, method, serviceType, out var fixedUpdateMiddleware)) app.UseFixedUpdate(fixedUpdateMiddleware);
			if (_tryGetSystemFunction<RenderAttribute>(app, method, serviceType, out var renderMiddleware)) app.UseRender(renderMiddleware);
			if (_tryGetSystemFunction<LastAttribute>(app, method, serviceType, out var lastMiddleware)) app.UseLast(lastMiddleware);
			if (_tryGetSystemFunction<DestroyAttribute>(app, method, serviceType, out var destroyMiddleware)) app.UseDestroy(destroyMiddleware);
		}

		return app;
	}

	private static bool _tryGetSystemFunction<TAttribute>(IKyberApplication app, MethodInfo method, Type serviceType, [NotNullWhen(true)] out Func<GameLoopDelegate, GameLoopDelegate>? middleware) where TAttribute : Attribute
	{
		middleware = null;
		var updateAttr = method.GetCustomAttribute<TAttribute>();
		if (updateAttr is null) return false;

		var parameters = method.GetParameters();

		if (typeof(GameLoopDelegate).IsAssignableTo(method.ReturnType) && parameters.Length == 1 && parameters[0].ParameterType == typeof(GameLoopDelegate))
		{
			var reflectionBinder = new NextGameLoopDelegateMiddlewareBinder(app, serviceType, method);
			middleware = reflectionBinder.CreateMiddleware;
			return true;
		}

		if (typeof(void).IsAssignableTo(method.ReturnType) && parameters.Length == 2 && parameters[0].ParameterType == typeof(GameTime) && parameters[1].ParameterType == typeof(GameLoopDelegate))
		{
			var reflectionBinder = new GameTimeNextVoidMiddlewareBinder(app, serviceType, method);
			middleware = reflectionBinder.CreateMiddleware;
			return true;
		}

		var foregroundColor = Console.ForegroundColor;
		Console.ForegroundColor = ConsoleColor.Yellow;
		Console.WriteLine($"Unknown middleware system type: {method.Name}({string.Join(", ", parameters.Select(p => p.Name ?? "??"))}): {method.ReturnType.Name}");
		Console.ForegroundColor = foregroundColor;

		return false;
	}

	private sealed class GameTimeNextVoidMiddlewareBinder
	{
		private readonly IKyberApplication _app;
		private readonly Type _systemType;
		private readonly MethodInfo _systemMethod;

		public GameTimeNextVoidMiddlewareBinder(IKyberApplication app, Type middlewareType, MethodInfo systemMethod)
		{
			_app = app;
			_systemType = middlewareType;
			_systemMethod = systemMethod;
		}

		// The CreateMiddleware method name is used by GameLoopBuilder to resolve the middleware type.
		public GameLoopDelegate CreateMiddleware(GameLoopDelegate next)
		{
			var instance = _app.Services.GetRequiredService(_systemType);

			var mw = (Action<GameTime, GameLoopDelegate>)_systemMethod.CreateDelegate(typeof(Action<GameTime, GameLoopDelegate>), instance);

			return (dt) => mw(dt, next);
		}
	}

	private sealed class NextGameLoopDelegateMiddlewareBinder
	{
		private readonly IKyberApplication _app;
		private readonly Type _systemType;
		private readonly MethodInfo _systemMethod;

		public NextGameLoopDelegateMiddlewareBinder(IKyberApplication app, Type middlewareType, MethodInfo systemMethod)
		{
			_app = app;
			_systemType = middlewareType;
			_systemMethod = systemMethod;
		}

		// The CreateMiddleware method name is used by GameLoopBuilder to resolve the middleware type.
		public GameLoopDelegate CreateMiddleware(GameLoopDelegate next)
		{
			var instance = _app.Services.GetRequiredService(_systemType);

			var @delegate = (Func<GameLoopDelegate, GameLoopDelegate>)_systemMethod.CreateDelegate(typeof(Func<GameLoopDelegate, GameLoopDelegate>), instance);

			return @delegate(next);
		}
	}
}
