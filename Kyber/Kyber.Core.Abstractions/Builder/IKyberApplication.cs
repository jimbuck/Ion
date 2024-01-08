using Microsoft.Extensions.Configuration;

namespace Kyber;

/// <summary>
/// The game application used to build the game loop.
/// </summary>
public interface IKyberApplication
{
	/// <summary>
	/// The application's configured <see cref="IConfiguration"/>.
	/// </summary>
	IConfiguration Configuration { get; }

	/// <summary>
	/// The application's configured services.
	/// </summary>
	IServiceProvider Services { get; }

	/// <summary>
	/// Adds the specified middleware to the application's Init pipeline.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IKyberApplication"/> instance to chain `Use` calls.</returns>
	IKyberApplication UseInit(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds the specified middleware to the application's First pipeline.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IKyberApplication"/> instance to chain `Use` calls.</returns>
	IKyberApplication UseFirst(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds the specified middleware to the application's FixedUpdate pipeline.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IKyberApplication"/> instance to chain `Use` calls.</returns>
	IKyberApplication UseFixedUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds the specified middleware to the application's Update pipeline.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IKyberApplication"/> instance to chain `Use` calls.</returns>
	IKyberApplication UseUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds the specified middleware to the application's Render pipeline.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IKyberApplication"/> instance to chain `Use` calls.</returns>
	IKyberApplication UseRender(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds the specified middleware to the application's Last pipeline.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IKyberApplication"/> instance to chain `Use` calls.</returns>
	IKyberApplication UseLast(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds the specified middleware to the application's Destroy pipeline.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IKyberApplication"/> instance to chain `Use` calls.</returns>
	IKyberApplication UseDestroy(Func<GameLoopDelegate, GameLoopDelegate> middleware);

	/// <summary>
	/// Builds and runs the game.
	/// </summary>
	void Run();
}