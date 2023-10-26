using Microsoft.Extensions.DependencyInjection;

using System.Reflection;

namespace Kyber.Builder;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class UpdateAttribute : Attribute { }

public static class UseMiddlewareExtensions
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
	public static GameApplication UseUpdateMiddleware<[DynamicallyAccessedMembers(MiddlewareAccessibility)] TMiddleware>(this GameApplication app, params object?[] args)
	{
		return app.UseUpdateMiddleware(typeof(TMiddleware), args);
	}

	/// <summary>
	/// Adds a middleware type to the application's request pipeline.
	/// </summary>
	/// <param name="app">The <see cref="IApplicationBuilder"/> instance.</param>
	/// <param name="middleware">The middleware type.</param>
	/// <param name="args">The arguments to pass to the middleware type instance's constructor.</param>
	/// <returns>The <see cref="IApplicationBuilder"/> instance.</returns>
	public static GameApplication UseUpdateMiddleware(
		this GameApplication app,
		[DynamicallyAccessedMembers(MiddlewareAccessibility)] Type middleware,
		params object?[] args)
	{
		var methods = middleware.GetMethods(BindingFlags.Instance | BindingFlags.Public);

		foreach (var method in methods)
		{
			var updateAttr = method.GetCustomAttribute<UpdateAttribute>();
			if (updateAttr is null) continue;

			if (!typeof(void).IsAssignableTo(method.ReturnType)) continue;

			var parameters = method.GetParameters();
			if (parameters.Length == 0 || (parameters.Length == 1 && parameters[0].ParameterType == typeof(GameTime)))
			{
				var reflectionBinder = new ReflectionMiddlewareBinder(app, middleware, args, method, parameters);
				app.UseUpdate(reflectionBinder.CreateMiddleware);
			}
		}

		return app;
	}

	private sealed class ReflectionMiddlewareBinder
	{
		private readonly GameApplication _app;
		[DynamicallyAccessedMembers(MiddlewareAccessibility)] private readonly Type _middleware;
		private readonly object?[] _args;
		private readonly MethodInfo _invokeMethod;
		private readonly ParameterInfo[] _parameters;

		public ReflectionMiddlewareBinder(
			GameApplication app,
			[DynamicallyAccessedMembers(MiddlewareAccessibility)] Type middleware,
			object?[] args,
			MethodInfo invokeMethod,
			ParameterInfo[] parameters)
		{
			_app = app;
			_middleware = middleware;
			_args = args;
			_invokeMethod = invokeMethod;
			_parameters = parameters;
		}

		// The CreateMiddleware method name is used by ApplicationBuilder to resolve the middleware type.
		public GameLoopDelegate CreateMiddleware(GameLoopDelegate next)
		{
			var ctorArgs = new object[_args.Length + 1];
			ctorArgs[0] = next;
			Array.Copy(_args, 0, ctorArgs, 1, _args.Length);
			var instance = ActivatorUtilities.CreateInstance(_app.Services, _middleware, ctorArgs);

			return (GameLoopDelegate)_invokeMethod.CreateDelegate(typeof(GameLoopDelegate), instance);
		}

		public override string ToString() => _middleware.ToString();
	}
}
