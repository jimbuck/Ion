using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Ion;

public class SystemMiddlewareBinder
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

		if (typeof(void).IsAssignableTo(method.ReturnType) && parameters.Length == 0)
		{
			var reflectionBinder = new AutoNextGameLoopDelegateMiddlewareBinder(services, serviceType, method);
			middleware = reflectionBinder.CreateMiddleware;
			return true;
		}

		var logger = services.GetRequiredService<ILogger<SystemMiddlewareBinder>>();

		logger.LogWarning("Unknown middleware system type: {ClassName}.{MethodName}({Params}): {ReturnType}", serviceType.FullName, method.Name, string.Join(", ", parameters.Select(p => p.ParameterType.Name + " " + (p.Name ?? "??"))), method.ReturnType.Name);

		return false;
	}

	private sealed class GameTimeNextVoidMiddlewareBinder(IServiceProvider services, [DynamicallyAccessedMembers(MiddlewareAccessibility)] Type middlewareType, MethodInfo systemMethod)
	{
		// The CreateMiddleware method name is used by GameLoopBuilder to resolve the middleware type.
		public GameLoopDelegate CreateMiddleware(GameLoopDelegate next)
		{
			var instance = services.GetRequiredService(middlewareType);

			var mw = (Action<GameTime, GameLoopDelegate>)systemMethod.CreateDelegate(typeof(Action<GameTime, GameLoopDelegate>), instance);

			return (dt) => mw(dt, next);
		}
	}

	private sealed class NextGameLoopDelegateMiddlewareBinder(IServiceProvider services, [DynamicallyAccessedMembers(MiddlewareAccessibility)] Type middlewareType, MethodInfo systemMethod)
	{
		// The CreateMiddleware method name is used by GameLoopBuilder to resolve the middleware type.
		public GameLoopDelegate CreateMiddleware(GameLoopDelegate next)
		{
			var instance = services.GetRequiredService(middlewareType);

			var @delegate = (Func<GameLoopDelegate, GameLoopDelegate>)systemMethod.CreateDelegate(typeof(Func<GameLoopDelegate, GameLoopDelegate>), instance);

			return @delegate(next);
		}
	}

	private sealed class AutoNextGameLoopDelegateMiddlewareBinder(IServiceProvider services, [DynamicallyAccessedMembers(MiddlewareAccessibility)] Type middlewareType, MethodInfo systemMethod)
	{
		// The CreateMiddleware method name is used by GameLoopBuilder to resolve the middleware type.
		public GameLoopDelegate CreateMiddleware(GameLoopDelegate next)
		{
			var instance = services.GetRequiredService(middlewareType);

			var @delegate = (Action)systemMethod.CreateDelegate(typeof(Action), instance);

			return (dt) =>
			{
				@delegate();
				next(dt);
			};
		}
	}
}
