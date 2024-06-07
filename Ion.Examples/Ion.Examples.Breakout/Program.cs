using System.Numerics;
using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Audio;

var builder = IonApplication.CreateBuilder(args);

builder.Services.AddIon(builder.Configuration, graphics =>
{
	graphics.Output = GraphicsOutput.Window;
	graphics.ClearColor = Color.DarkSlateGray;
});

builder.Services.AddSingleton<BreakoutSystems>();

var game = builder.Build();
game.UseIon()
	.UseSystem<BreakoutSystems>();

//Thread.Sleep(10 * 1000); // Delay to let diagnostics warm up.

game.Run();


public class BreakoutSystems(IWindow window, IInputState input, ISpriteBatch spriteBatch, IEventListener events, IAssetManager assets, IAudioManager audio)
{
	private const int ROWS = 10;
	private const int COLS = 10;

	private readonly bool[] _blockStates = new bool[ROWS * COLS];
	private readonly Color[] _blockColors = new Color[ROWS * COLS];
	private readonly RectangleF[] _blockRects = new RectangleF[ROWS * COLS];

	private Vector2 _blockSize = new(100, 25f);
	private readonly float _blockGap = 10f;
	private readonly float _playerGap = 150f;
	private readonly float _bottomGap = 20f;

	private RectangleF _playerRect = new(0, 0, 160, 20f);

	private RectangleF _ballRect = new(0, 0, 20f, 20f);
	private Vector2 _ballVelocity = Vector2.Zero;
	private readonly float _initialBallSpeed = 200f;
	private float _ballSpeed = 200f;
	private bool _ballIsCaptured = true;

	private readonly Random _rand = new(6014);

	private readonly Vector2 _paddleBounceMin = Vector2.Normalize(new Vector2(-1, -0.75f));
	private readonly Vector2 _paddleBounceMax = Vector2.Normalize(new Vector2(+1, -0.75f));

	private Texture2D _blockTexture = default!;
	private Texture2D _ballTexture = default!;
	private readonly RectangleF _ballSprite = new RectangleF(1, 1, 14, 14);

	private SoundEffect _bonkSound = default!;
	private SoundEffect _pingSound = default!;

	private int _score = 0;
	private FontSet _scoreFontSet = default!;
	private Font _scoreFont = default!;

	[Init]
	public void SetupBlocks(GameTime dt, GameLoopDelegate next)
	{		
		_blockTexture = assets.Load<Texture2D>("Block1.png");
		_ballTexture = assets.Load<Texture2D>("Ball1.png");
		_bonkSound =  assets.Load<SoundEffect>("Bonk.wav");
		_pingSound =  assets.Load<SoundEffect>("Ping.mp3");
		_scoreFontSet = assets.Load<FontSet>("BungeeRegular", "Bungee-Regular.ttf");
		_scoreFont = _scoreFontSet.CreateStyle(24);

		// Setup blocks in rows and columns across the window each with different colors:
		for (int row = 0; row < ROWS; row++)
		{
			for (int col = 0; col < COLS; col++)
			{
				var i = (row * COLS) + col;
				_blockStates[i] = true;
				_blockColors[i] = Color.Lerp(Color.LightSeaGreen, Color.DarkGreen, (float)row / ROWS);
				_blockRects[i] = new RectangleF(Vector2.Zero, _blockSize);
			}
		}

		window.Size = new Vector2((COLS * _blockSize.X) + ((COLS + 1) * _blockGap), (ROWS * _blockSize.Y) + ((ROWS + 1) * _blockGap) + _playerGap + _blockSize.Y + _bottomGap);
		window.IsResizable = false;

		_playerRect.Location = new Vector2(Math.Clamp(input.MousePosition.X - (_playerRect.Height / 2f), 0, window.Width - _playerRect.Width), window.Size.Y - (_blockSize.Y + _bottomGap));

		_repositionBlocks();

		next(dt);
	}

	[First]
	public void HandleWindowResize(GameTime dt, GameLoopDelegate next)
	{
		if (events.On<WindowResizeEvent>()) _repositionBlocks();

		next(dt);
	}

	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		var isMouseGrabbed = window.IsMouseGrabbed;

		if (input.Pressed(Key.Escape) && isMouseGrabbed)
		{
			window.IsMouseGrabbed = false;
			window.IsCursorVisible = true;
		}

		if (input.Pressed(MouseButton.Left) && !isMouseGrabbed)
		{
			window.IsMouseGrabbed = true;
			window.IsCursorVisible = false;
		}


		if (isMouseGrabbed) _playerRect.X = Math.Clamp(input.MousePosition.X - (_playerRect.Height / 2f), 0, window.Width - _playerRect.Width);

		if (_ballIsCaptured)
		{
			_ballRect.Location = _playerRect.Location + new Vector2((_playerRect.Width - _ballRect.Width) / 2f, -(_ballRect.Height + 1));

			if (input.Pressed(MouseButton.Left) && isMouseGrabbed)
			{
				_ballIsCaptured = false;
				_ballVelocity = new Vector2(0, -1);
			}
		}

		// Ball movement
		_ballRect.Location += _ballVelocity * _ballSpeed * dt;

		// Window border collisions
		var hitWall = false;
		if (_ballRect.X <= 0)
		{
			_ballRect.X = 1;
			_ballVelocity.X *= -1;
			hitWall = true;
		}

		if (_ballRect.X >= window.Width - _ballRect.Width)
		{
			_ballRect.X = window.Width - (_ballRect.Width + 1);
			_ballVelocity.X *= -1;
			hitWall = true;
		}

		if (_ballRect.Top <= 0)
		{
			_ballRect.Y = 1;
			_ballVelocity.Y *= -1;
			hitWall = true;
		}

		if (_ballRect.Y > window.Height)
		{
			_ballVelocity = Vector2.Zero;
			_ballIsCaptured = true;
			_ballSpeed = _initialBallSpeed;
		}

		// Ball to player collisions
		if (_ballRect.Bottom > 250 && _ballVelocity.Y > 0)
		{
			RectangleF.Intersect(ref _ballRect, ref _playerRect, out var intersection);

			if (intersection.IsEmpty is false)
			{
				hitWall = true;
				_ballSpeed += 5f;
				_handlePlayerCollision(ref intersection);
			}
		}

		if (hitWall) audio.Play(_bonkSound, pitchShift: (_rand.NextSingle() - 0.5f) / 16f);

		// Ball to block collisions
		for (var i = 0; i < _blockRects.Length; i++)
		{
			if (!_blockStates[i]) continue;

			RectangleF.Intersect(ref _ballRect, ref _blockRects[i], out var intersection);
			if (intersection.IsEmpty) continue;

			audio.Play(_pingSound, pitchShift: (_rand.NextSingle() - 0.5f) / 4f);
			_ballSpeed += 10f;
			_blockStates[i] = false;
			_ballVelocity *= _getReboundDirection(ref intersection);
			_ballVelocity = Vector2.Normalize(_ballVelocity);
			_score += 10;
			break;
		}

		var hasBlocksLeft = false;
		foreach(var blockstate in _blockStates)
		{
			if (blockstate is true)
			{
				hasBlocksLeft = true;
				break;
			}
		}

		if (hasBlocksLeft is false)
		{
			_ballIsCaptured = true;
			_ballVelocity = Vector2.Zero;
			_ballSpeed = _initialBallSpeed;
			for (int row = 0; row < ROWS; row++)
			{
				for (int col = 0; col < COLS; col++)
				{
					var i = (row * COLS) + col;
					_blockStates[i] = true;
				}
			}

			Console.WriteLine("You Win!");
		}

		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		for (var row = 0; row < ROWS; row++)
		{
			for (var col = 0; col < COLS; col++)
			{
				var i = (row * COLS) + col;
				if (_blockStates[i]) spriteBatch.Draw(_blockTexture, _blockRects[i], color: _blockColors[i]);
			}
		}

		spriteBatch.Draw(_blockTexture, _playerRect, color: Color.DarkBlue);
		spriteBatch.Draw(_ballTexture, _ballRect, color: Color.DarkRed, sourceRectangle: _ballSprite);
		spriteBatch.DrawString(_scoreFont, $"Score:  {_score}", new Vector2(20f), Color.Red);

		next(dt);
	}

	private void _repositionBlocks()
	{
		var edgeOffset = new Vector2(_blockGap);
		for (int row = 0; row < ROWS; row++)
		{
			for (int col = 0; col < COLS; col++)
			{
				_blockRects[(row * COLS) + col].Location = edgeOffset + new Vector2(col * (_blockSize.X + _blockGap), row * (_blockSize.Y + _blockGap));
			}
		}
	}

	private void _handlePlayerCollision(ref RectangleF intersection)
	{
		if (_ballIsCaptured) return;

		// Left or right side
		if (intersection.Top != _ballRect.Top && intersection.Bottom != _ballRect.Bottom)
		{
			_ballVelocity.X *= -1f;
			_ballVelocity = Vector2.Normalize(_ballVelocity);
			_ballRect.X = intersection.Left == _playerRect.Left ? _playerRect.X - (_ballRect.Width+1) : _playerRect.Right + 1;
			return;
		}

		_ballVelocity.Y *= -1f;
		var offsetFromPaddle = (_ballRect.Left - _playerRect.X) / (_playerRect.Width - _ballRect.Width);

		_ballVelocity = Vector2.Lerp(_paddleBounceMin, _paddleBounceMax, offsetFromPaddle);

		_ballVelocity = Vector2.Normalize(_ballVelocity);
	}

	private Vector2 _getReboundDirection(ref RectangleF intersection)
	{
		if (intersection.Top != _ballRect.Top && intersection.Bottom != _ballRect.Bottom) return new Vector2(-1, 1);
		if (intersection.Left != _ballRect.Left && intersection.Right != _ballRect.Right) return new Vector2(1, -1);

		if (intersection.Width > intersection.Height) return new Vector2(1, -1);
		if (intersection.Width < intersection.Height) return new Vector2(-1, 1);

		return Vector2.One * -1;
	}
}