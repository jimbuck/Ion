using Microsoft.Extensions.Configuration;

namespace Kyber;

public interface IKyberApplication
{
	IConfiguration Configuration { get; }
	IServiceProvider Services { get; }

	IKyberApplication UseInit(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	IKyberApplication UseFirst(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	IKyberApplication UseFixedUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	IKyberApplication UseUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	IKyberApplication UseRender(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	IKyberApplication UseLast(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	IKyberApplication UseDestroy(Func<GameLoopDelegate, GameLoopDelegate> middleware);
}
