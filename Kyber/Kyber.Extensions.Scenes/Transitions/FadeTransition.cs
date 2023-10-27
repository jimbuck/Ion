using System.Numerics;

namespace Kyber.Scenes.Transitions;

/// <summary>
/// A standard fade-to-black transition.
/// </summary>
//public class FadeTransition : Transition
//{
//    private readonly GraphicsDevice _graphicsDevice;
//    private readonly SpriteBatch _spriteBatch;

//    private static Texture2D? _whitePixelTexture;

//    private static Texture2D _getTexture(SpriteBatch spriteBatch)
//    {
//        if (_whitePixelTexture == null)
//        {
//            _whitePixelTexture = new Texture2D(spriteBatch.GraphicsDevice, 1, 1, false, SurfaceFormat.Color);
//            _whitePixelTexture.SetData(new[] { Color.White });
//            spriteBatch.Disposing += (sender, args) =>
//            {
//                _whitePixelTexture?.Dispose();
//                _whitePixelTexture = null;
//            };
//        }

//        return _whitePixelTexture;
//    }

//    /// <summary>
//    /// Creates a new fade transition using a dedicated sprite batch.
//    /// </summary>
//    /// <param name="graphicsDevice">The graphics device to create a sprite batch from.</param>
//    public FadeTransition(GraphicsDevice graphicsDevice) : base()
//    {
//        _graphicsDevice = graphicsDevice;
//        _spriteBatch = new SpriteBatch(graphicsDevice, 64) { Name = $"{nameof(FadeTransition)}SpriteBatch" };
//    }

//    /// <inheritdoc/>
//    public override void Dispose()
//    {
//        _spriteBatch.Dispose();
//    }

//    /// <inheritdoc/>
//    public override void Render(float dt)
//    {
//        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
//        _spriteBatch.Draw(_getTexture(_spriteBatch), Vector2.Zero, new Rectangle(0, 0, _graphicsDevice.Viewport.Width, _graphicsDevice.Viewport.Height), Color.Black * Value);
//        _spriteBatch.End();
//    }
//}
