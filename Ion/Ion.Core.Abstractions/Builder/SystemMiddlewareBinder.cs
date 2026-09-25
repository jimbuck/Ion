using System.Diagnostics.CodeAnalysis;

namespace Ion;

/// <summary>
/// Shared trimming annotations for system types. Binding itself lives in <see cref="ScheduleModel"/> and
/// <see cref="Schedule"/>.
/// </summary>
public static class SystemMiddlewareBinder
{
	/// <summary>
	/// The members of a system type that binding reads: public constructors (for DI), public methods (the steps) and
	/// non-public methods (to report stage attributes on methods that could never run, ION004).
	/// </summary>
	public const DynamicallyAccessedMemberTypes MiddlewareAccessibility = DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods;
}
