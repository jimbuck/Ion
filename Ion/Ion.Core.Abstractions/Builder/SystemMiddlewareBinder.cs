using Microsoft.Extensions.DependencyInjection;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Ion;

public static class SystemMiddlewareBinder
{
	// We're going to keep all public constructors and public methods on middleware
	public const DynamicallyAccessedMemberTypes MiddlewareAccessibility = DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods;

	public static bool TryGetSystemFunction<TAttribute>(IServiceProvider services, MethodInfo method, [DynamicallyAccessedMembers(MiddlewareAccessibility)] Type serviceType, [NotNullWhen(true)] out Func<GameLoopDelegate, GameLoopDelegate>? middleware) where TAttribute : Attribute
	{
		middleware = null;
		var updateAttr = method.GetCustomAttribute<TAttribute>();
		if (updateAttr is null) return false;

		var parameters = method.GetParameters();

		if (typeof(GameLoopDelegate).IsAssignableTo(method.ReturnType) && parameters.Length == 1 && parameters[0].ParameterType == typeof(GameLoopDelegate))
		{
			var reflectionBinder = new NextGameLoopDelegateMiddlewareBinder(services, serviceType, method);
			middleware = reflectionBinder.CreateMiddleware;
			return true;
		}

		if (typeof(void).IsAssignableTo(method.ReturnType) && parameters.Length == 2 && parameters[0].ParameterType == typeof(GameTime) && parameters[1].ParameterType == typeof(GameLoopDelegate))
		{
			var reflectionBinder = new GameTimeNextVoidMiddlewareBinder(services, serviceType, method);
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
		private readonly IServiceProvider _services;
		private readonly Type _systemType;
		private readonly MethodInfo _systemMethod;

		public GameTimeNextVoidMiddlewareBinder(IServiceProvider services, [DynamicallyAccessedMembers(MiddlewareAccessibility)] Type middlewareType, MethodInfo systemMethod)
		{
			_services = services;
			_systemType = middlewareType;
			_systemMethod = systemMethod;
		}

		// The CreateMiddleware method name is used by GameLoopBuilder to resolve the middleware type.
		public GameLoopDelegate CreateMiddleware(GameLoopDelegate next)
		{
			var instance = _services.GetRequiredService(_systemType);

			var mw = (Action<GameTime, GameLoopDelegate>)_systemMethod.CreateDelegate(typeof(Action<GameTime, GameLoopDelegate>), instance);

			return (dt) => mw(dt, next);
		}
	}

	private sealed class NextGameLoopDelegateMiddlewareBinder
	{
		private readonly IServiceProvider _services;
		private readonly Type _systemType;
		private readonly MethodInfo _systemMethod;

		public NextGameLoopDelegateMiddlewareBinder(IServiceProvider services, [DynamicallyAccessedMembers(MiddlewareAccessibility)] Type middlewareType, MethodInfo systemMethod)
		{
			_services = services;
			_systemType = middlewareType;
			_systemMethod = systemMethod;
		}

		// The CreateMiddleware method name is used by GameLoopBuilder to resolve the middleware type.
		public GameLoopDelegate CreateMiddleware(GameLoopDelegate next)
		{
			var instance = _services.GetRequiredService(_systemType);

			var @delegate = (Func<GameLoopDelegate, GameLoopDelegate>)_systemMethod.CreateDelegate(typeof(Func<GameLoopDelegate, GameLoopDelegate>), instance);

			return @delegate(next);
		}
	}
}
