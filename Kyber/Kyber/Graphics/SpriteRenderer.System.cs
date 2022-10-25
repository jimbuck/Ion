namespace Kyber.Graphics;

public class SpriteRendererBeginSystem : IInitializeSystem, IPreRenderSystem
{
	private readonly SpriteRenderer _spriteBatch;

	public bool IsEnabled { get; set; } = true;

	public SpriteRendererBeginSystem(ISpriteRenderer spriteBatch)
	{
		_spriteBatch = (SpriteRenderer)spriteBatch;
	}

	public void Initialize()
	{
		_spriteBatch.Initialize();
	}

	public void PreRender(float dt)
	{
		_spriteBatch.Begin();
	}
}

public class SpriteRendererEndSystem : IPostRenderSystem
{
	private readonly ISpriteRenderer _spriteBatch;

	public bool IsEnabled { get; set; } = true;

	public SpriteRendererEndSystem(ISpriteRenderer spriteBatch)
	{
		_spriteBatch = spriteBatch;
	}

	public void PostRender(float dt)
	{
		_spriteBatch.End();
	}
}