using Microsoft.Extensions.Configuration;

namespace Ion.Extensions.Scenes;

public interface ISceneBuilder
{
	int SceneId { get; }

	IConfiguration Configuration { get; }
	IServiceProvider Services { get; }

	ISceneBuilder UseInit(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	ISceneBuilder UseFirst(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	ISceneBuilder UseFixedUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	ISceneBuilder UseUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	ISceneBuilder UseRender(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	ISceneBuilder UseLast(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	ISceneBuilder UseDestroy(Func<GameLoopDelegate, GameLoopDelegate> middleware);
}