namespace Kyber;

public static class BuilderExtensions
{
	public static IKyberApplication UseEvents(this IKyberApplication app)
	{
		return app.UseSystem<EventSystem>();
	}
}
