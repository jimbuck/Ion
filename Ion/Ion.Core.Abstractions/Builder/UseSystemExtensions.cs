using System.Diagnostics.CodeAnalysis;

namespace Ion;

/// <summary>
/// Registers systems on the application's root schedule.
/// </summary>
public static class UseSystemExtensions
{
	private const DynamicallyAccessedMemberTypes SystemAccessibility = SystemMiddlewareBinder.MiddlewareAccessibility;

	/// <summary>
	/// Adds a system to the application's schedule: every public method of <paramref name="systemType"/> with a stage
	/// attribute becomes a step, and every <see cref="BeginAttribute"/>/<see cref="EndAttribute"/> pair a scope. Steps run
	/// by <see cref="StageAttribute.Order"/>, then registration order, so systems can be added in any order relative to
	/// the engine's. The instance is resolved from the application's services when the schedule is built.
	/// </summary>
	public static IIonApplication UseSystem(this IIonApplication app, [DynamicallyAccessedMembers(SystemAccessibility)] Type systemType)
	{
		return UseSystem(app, systemType, systemType);
	}

	/// <inheritdoc cref="UseSystem(IIonApplication, Type)"/>
	public static IIonApplication UseSystem<[DynamicallyAccessedMembers(SystemAccessibility)] TSystem>(this IIonApplication app)
	{
		return UseSystem(app, typeof(TSystem), typeof(TSystem));
	}

	/// <summary>
	/// Adds a system resolved as <typeparamref name="TService"/> whose steps are the methods of
	/// <typeparamref name="TImplementation"/>.
	/// </summary>
	public static IIonApplication UseSystem<[DynamicallyAccessedMembers(SystemAccessibility)] TService, [DynamicallyAccessedMembers(SystemAccessibility)] TImplementation>(this IIonApplication app)
	{
		return UseSystem(app, typeof(TService), typeof(TImplementation));
	}

	/// <summary>
	/// Adds a system resolved as <paramref name="serviceType"/> whose steps are the methods of
	/// <paramref name="implementationType"/>.
	/// </summary>
	public static IIonApplication UseSystem(this IIonApplication app, [DynamicallyAccessedMembers(SystemAccessibility)] Type serviceType, [DynamicallyAccessedMembers(SystemAccessibility)] Type implementationType)
	{
		ArgumentNullException.ThrowIfNull(app);
		app.Schedule.AddSystem(serviceType, implementationType);
		return app;
	}
}
