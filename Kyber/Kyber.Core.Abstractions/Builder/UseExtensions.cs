
namespace Kyber.Builder;

public static class UseExtensions
{
	public static IKyberApplication UseInit(this IKyberApplication app, Action<GameTime, GameLoopDelegate> middleware) => app.UseInit(next => dt => middleware(dt, next));

	public static IKyberApplication UseFirst(this IKyberApplication app, Action<GameTime, GameLoopDelegate> middleware) => app.UseFirst(next => dt => middleware(dt, next));

	public static IKyberApplication UseFixedUpdate(this IKyberApplication app, Action<GameTime, GameLoopDelegate> middleware) => app.UseFixedUpdate(next => dt => middleware(dt, next));

	public static IKyberApplication UseUpdate(this IKyberApplication app, Action<GameTime, GameLoopDelegate> middleware) => app.UseUpdate(next => dt => middleware(dt, next));

	public static IKyberApplication UseRender(this IKyberApplication app, Action<GameTime, GameLoopDelegate> middleware) => app.UseRender(next => dt => middleware(dt, next));

	public static IKyberApplication UseLast(this IKyberApplication app, Action<GameTime, GameLoopDelegate> middleware) => app.UseLast(next => dt => middleware(dt, next));

	public static IKyberApplication UseDestroy(this IKyberApplication app, Action<GameTime, GameLoopDelegate> middleware) => app.UseDestroy(next => dt => middleware(dt, next));

}
