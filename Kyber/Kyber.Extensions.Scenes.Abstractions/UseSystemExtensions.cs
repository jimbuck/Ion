using Microsoft.Extensions.DependencyInjection;
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
	/// <param name="scene">The <see cref="ISceneBuilder"/> instance.</param>
	/// <param name="args">The arguments to pass to the middleware type instance's constructor.</param>
	/// <returns>The <see cref="ISceneBuilder"/> instance.</returns>
	public static ISceneBuilder UseSystem<[DynamicallyAccessedMembers(MiddlewareAccessibility)] TMiddleware>(this ISceneBuilder scene)
	{
		return UseSystem(scene, typeof(TMiddleware));
	}

	/// <summary>
	/// Adds a middleware type to the application's request pipeline.
	/// </summary>
	/// <param name="scene">The <see cref="ISceneBuilder"/> instance.</param>
	/// <param name="middlewareType">The middleware type.</param>
	/// <param name="args">The arguments to pass to the middleware type instance's constructor.</param>
	/// <returns>The <see cref="ISceneBuilder"/> instance.</returns>
	public static ISceneBuilder UseSystem(this ISceneBuilder scene, [DynamicallyAccessedMembers(MiddlewareAccessibility)] Type middlewareType)
	{
		var methods = middlewareType.GetMethods(BindingFlags.Instance | BindingFlags.Public);

		foreach (var method in methods)
		{
			if (_tryGetSystemFunction<InitAttribute>(scene, method, middlewareType, out var initMiddleware)) scene.UseInit(initMiddleware);
			if (_tryGetSystemFunction<FirstAttribute>(scene, method, middlewareType, out var firstMiddleware)) scene.UseFirst(firstMiddleware);
			if (_tryGetSystemFunction<UpdateAttribute>(scene, method, middlewareType, out var updateMiddleware)) scene.UseUpdate(updateMiddleware);
			if (_tryGetSystemFunction<FixedUpdateAttribute>(scene, method, middlewareType, out var fixedUpdateMiddleware)) scene.UseFixedUpdate(fixedUpdateMiddleware);
			if (_tryGetSystemFunction<RenderAttribute>(scene, method, middlewareType, out var renderMiddleware)) scene.UseRender(renderMiddleware);
			if (_tryGetSystemFunction<LastAttribute>(scene, method, middlewareType, out var lastMiddleware)) scene.UseLast(lastMiddleware);
			if (_tryGetSystemFunction<DestroyAttribute>(scene, method, middlewareType, out var destroyMiddleware)) scene.UseDestroy(destroyMiddleware);
		}

		return scene;
	}

	private static bool _tryGetSystemFunction<TAttribute>(ISceneBuilder scene, MethodInfo method, [DynamicallyAccessedMembers(MiddlewareAccessibility)] Type middlewareType, [NotNullWhen(true)] out Func<GameLoopDelegate, GameLoopDelegate>? middleware) where TAttribute : Attribute
	{
		middleware = null;
		var updateAttr = method.GetCustomAttribute<TAttribute>();
		if (updateAttr is null) return false;

		var parameters = method.GetParameters();

		if (typeof(GameLoopDelegate).IsAssignableTo(method.ReturnType) && parameters.Length == 1 && parameters[0].ParameterType == typeof(GameLoopDelegate))
		{
			var reflectionBinder = new NextGameLoopDelegateMiddlewareBinder(scene, middlewareType, method);
			middleware = reflectionBinder.CreateMiddleware;
			return true;
		}

		if (typeof(void).IsAssignableTo(method.ReturnType) && parameters.Length == 2 && parameters[0].ParameterType == typeof(GameTime) && parameters[1].ParameterType == typeof(GameLoopDelegate))
		{
			var reflectionBinder = new GameTimeNextVoidMiddlewareBinder(scene, middlewareType, method);
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
		private readonly ISceneBuilder _scene;
		[DynamicallyAccessedMembers(MiddlewareAccessibility)]
		private readonly Type _systemType;
		private readonly MethodInfo _systemMethod;

		public GameTimeNextVoidMiddlewareBinder(ISceneBuilder scene, [DynamicallyAccessedMembers(MiddlewareAccessibility)] Type middlewareType, MethodInfo systemMethod)
		{
			_scene = scene;
			_systemType = middlewareType;
			_systemMethod = systemMethod;
		}

		// The CreateMiddleware method name is used by GameLoopBuilder to resolve the middleware type.
		public GameLoopDelegate CreateMiddleware(GameLoopDelegate next)
		{
			var instance = _scene.Services.GetRequiredService(_systemType);

			var mw = (Action<GameTime, GameLoopDelegate>)_systemMethod.CreateDelegate(typeof(Action<GameTime, GameLoopDelegate>), instance);

			return (dt) => mw(dt, next);
		}
	}

	private sealed class NextGameLoopDelegateMiddlewareBinder
	{
		private readonly ISceneBuilder _scene;
		[DynamicallyAccessedMembers(MiddlewareAccessibility)]
		private readonly Type _systemType;
		private readonly MethodInfo _systemMethod;

		public NextGameLoopDelegateMiddlewareBinder(ISceneBuilder scene, [DynamicallyAccessedMembers(MiddlewareAccessibility)] Type middlewareType, MethodInfo systemMethod)
		{
			_scene = scene;
			_systemType = middlewareType;
			_systemMethod = systemMethod;
		}

		// The CreateMiddleware method name is used by GameLoopBuilder to resolve the middleware type.
		public GameLoopDelegate CreateMiddleware(GameLoopDelegate next)
		{
			var instance = _scene.Services.GetRequiredService(_systemType);

			var @delegate = (Func<GameLoopDelegate, GameLoopDelegate>)_systemMethod.CreateDelegate(typeof(Func<GameLoopDelegate, GameLoopDelegate>), instance);

			return @delegate(next);
		}
	}
}
