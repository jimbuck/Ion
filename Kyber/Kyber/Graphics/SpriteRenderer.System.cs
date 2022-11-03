using Veldrid;

namespace Kyber.Graphics;

public class SpriteRendererBeginSystem : IInitializeSystem, IPreRenderSystem
{
	private readonly SpriteRenderer _spriteBatch;
	private readonly IGraphicsDevice _graphicsDevice;

	public bool IsEnabled { get; set; } = true;

	public SpriteRendererBeginSystem(ISpriteRenderer spriteBatch, IGraphicsDevice graphicsDevice)
	{
		_spriteBatch = (SpriteRenderer)spriteBatch;
		_graphicsDevice = graphicsDevice;
	}

	public void Initialize()
	{
		if (_graphicsDevice.NoRender) return;
		_spriteBatch.Initialize();
	}

	public void PreRender(float dt)
	{
		if (_graphicsDevice.NoRender) return;
		_spriteBatch.Begin();
	}
}

public class SpriteRendererEndSystem : IPostRenderSystem
{
	private readonly ISpriteRenderer _spriteBatch;
	private readonly IGraphicsDevice _graphicsDevice;

	public bool IsEnabled { get; set; } = true;

	public SpriteRendererEndSystem(ISpriteRenderer spriteBatch, IGraphicsDevice graphicsDevice)
	{
		_spriteBatch = spriteBatch;
		_graphicsDevice = graphicsDevice;
	}

	public void PostRender(float dt)
	{
		if (_graphicsDevice.NoRender) return;
		_spriteBatch.End();
	}
}