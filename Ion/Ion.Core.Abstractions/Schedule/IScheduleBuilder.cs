namespace Ion;

/// <summary>
/// Something steps can be registered on: the application (root schedule) or a scene. The <c>UseSystem</c> and function
/// step extensions (<c>Update(...)</c>, <c>Render(...)</c>, ...) add to <see cref="Schedule"/>.
/// </summary>
public interface IScheduleBuilder
{
	/// <summary>The services steps are resolved from.</summary>
	IServiceProvider Services { get; }

	/// <summary>The registrations of this schedule.</summary>
	ScheduleModel Schedule { get; }
}
