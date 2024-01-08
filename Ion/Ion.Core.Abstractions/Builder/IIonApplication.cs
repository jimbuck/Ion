using Microsoft.Extensions.Configuration;

namespace Ion;

/// <summary>
/// The game application used to build the game loop.
/// </summary>
public interface IIonApplication
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
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseInit(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds the specified middleware to the application's First pipeline.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseFirst(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds the specified middleware to the application's FixedUpdate pipeline.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseFixedUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds the specified middleware to the application's Update pipeline.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds the specified middleware to the application's Render pipeline.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseRender(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds the specified middleware to the application's Last pipeline.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseLast(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds the specified middleware to the application's Destroy pipeline.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseDestroy(Func<GameLoopDelegate, GameLoopDelegate> middleware);

	/// <summary>
	/// Builds and runs the game.
	/// </summary>
	void Run();
}