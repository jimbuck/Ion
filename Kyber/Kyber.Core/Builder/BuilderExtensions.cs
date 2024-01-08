namespace Kyber;

public static class BuilderExtensions
{
	/// <summary>
	/// Adds the EventSystem to the application's middleware pipeline.
	/// </summary>
	/// <param name="app">The <see cref="IKyberApplication"/> instance.</param>
	/// <returns>The <see cref="IKyberApplication"/> instance.</returns>
	public static IKyberApplication UseEvents(this IKyberApplication app)
	{
		return app.UseSystem<EventSystem>();
	}
}
