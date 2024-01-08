namespace Ion;

public static class BuilderExtensions
{
	/// <summary>
	/// Adds the EventSystem to the application's middleware pipeline.
	/// </summary>
	/// <param name="app">The <see cref="IIonApplication"/> instance.</param>
	/// <returns>The <see cref="IIonApplication"/> instance.</returns>
	public static IIonApplication UseEvents(this IIonApplication app)
	{
		return app.UseSystem<EventSystem>();
	}
}
