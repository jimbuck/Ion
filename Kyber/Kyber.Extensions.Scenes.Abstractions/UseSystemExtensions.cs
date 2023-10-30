using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace Kyber.Builder;

public static class UseSystemExtensions
{
	/// <summary>
	/// Adds a middleware type to the application's request pipeline.
	/// </summary>
	/// <typeparam name="TMiddleware">The middleware type.</typeparam>
	/// <param name="scene">The <see cref="ISceneBuilder"/> instance.</param>
	/// <param name="args">The arguments to pass to the middleware type instance's constructor.</param>
	/// <returns>The <see cref="ISceneBuilder"/> instance.</returns>
	public static ISceneBuilder UseSystem<[DynamicallyAccessedMembers(SystemMiddlewareBinder.MiddlewareAccessibility)] TMiddleware>(this ISceneBuilder scene)
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
	public static ISceneBuilder UseSystem(this ISceneBuilder scene, [DynamicallyAccessedMembers(SystemMiddlewareBinder.MiddlewareAccessibility)] Type middlewareType)
	{
		var methods = middlewareType.GetMethods(BindingFlags.Instance | BindingFlags.Public);

		foreach (var method in methods)
		{
			if (SystemMiddlewareBinder.TryGetSystemFunction<InitAttribute>(scene.Services, method, middlewareType, out var initMiddleware)) scene.UseInit(initMiddleware);
			if (SystemMiddlewareBinder.TryGetSystemFunction<FirstAttribute>(scene.Services, method, middlewareType, out var firstMiddleware)) scene.UseFirst(firstMiddleware);
			if (SystemMiddlewareBinder.TryGetSystemFunction<UpdateAttribute>(scene.Services, method, middlewareType, out var updateMiddleware)) scene.UseUpdate(updateMiddleware);
			if (SystemMiddlewareBinder.TryGetSystemFunction<FixedUpdateAttribute>(scene.Services, method, middlewareType, out var fixedUpdateMiddleware)) scene.UseFixedUpdate(fixedUpdateMiddleware);
			if (SystemMiddlewareBinder.TryGetSystemFunction<RenderAttribute>(scene.Services, method, middlewareType, out var renderMiddleware)) scene.UseRender(renderMiddleware);
			if (SystemMiddlewareBinder.TryGetSystemFunction<LastAttribute>(scene.Services, method, middlewareType, out var lastMiddleware)) scene.UseLast(lastMiddleware);
			if (SystemMiddlewareBinder.TryGetSystemFunction<DestroyAttribute>(scene.Services, method, middlewareType, out var destroyMiddleware)) scene.UseDestroy(destroyMiddleware);
		}

		return scene;
	}
}
