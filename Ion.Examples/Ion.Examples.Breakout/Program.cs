using System.Numerics;
using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Extensions.Graphics;

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


public class BreakoutSystems
{
	private readonly IWindow _window;
	private readonly IInputState _input;
	private readonly ISpriteBatch _spriteBatch;
	private readonly IEventListener _events;
	private readonly IAssetManager _assets;

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

	private readonly Vector2 _paddleBounceMin = Vector2.Normalize(new Vector2(-1, -0.75f));
	private readonly Vector2 _paddleBounceMax = Vector2.Normalize(new Vector2(+1, -0.75f));

	private Texture2D _blockTexture;
	private Texture2D _ballTexture;

	public BreakoutSystems(IWindow window, IInputState input, ISpriteBatch spriteBatch, IEventListener events, IAssetManager assets)
	{
		_window = window;
		_input = input;
		_spriteBatch = spriteBatch;
		_events = events;
		_assets = assets;
	}

	[Init]
	public void SetupBlocks(GameTime dt, GameLoopDelegate next)
	{
		_blockTexture = _assets.Load<Texture2D>("Block1.png");
		_ballTexture = _assets.Load<Texture2D>("Ball1.png");

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

		_window.Size = new Vector2((COLS * _blockSize.X) + ((COLS + 1) * _blockGap), (ROWS * _blockSize.Y) + ((ROWS + 1) * _blockGap) + _playerGap + _blockSize.Y + _bottomGap);
		_window.IsResizable = false;

		_playerRect.Location = new Vector2(Math.Clamp(_input.MousePosition.X - (_playerRect.Height / 2f), 0, _window.Width - _playerRect.Width), _window.Size.Y - (_blockSize.Y + _bottomGap));

		_repositionBlocks();

		next(dt);
	}

	[First]
	public void HandleWindowResize(GameTime dt, GameLoopDelegate next)
	{
		if (_events.On<WindowResizeEvent>()) _repositionBlocks();

		next(dt);
	}

	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		_playerRect.X = Math.Clamp(_input.MousePosition.X - (_playerRect.Height / 2f), 0, _window.Width - _playerRect.Width);

		if (_ballIsCaptured)
		{
			_ballRect.Location = _playerRect.Location + new Vector2((_playerRect.Width - _ballRect.Width) / 2f, -(_ballRect.Height + 1));

			if (_input.Pressed(MouseButton.Left))
			{
				_ballIsCaptured = false;
				_ballVelocity = new Vector2(0, -1);
			}
		}

		// Ball movement
		_ballRect.Location += _ballVelocity * _ballSpeed * dt;

		// WIndow border collisions
		if (_ballRect.X <= 0)
		{
			_ballRect.X = 0;
			_ballVelocity.X *= -1;
		}

		if (_ballRect.X >= _window.Width - _ballRect.Width)
		{
			_ballRect.X = _window.Width - _ballRect.Width;
			_ballVelocity.X *= -1;
		}

		if (_ballRect.Top <= 0)
		{
			_ballRect.Y = 0;
			_ballVelocity.Y *= -1;
		}

		if (_ballRect.Y > _window.Height)
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
				_ballSpeed += 1f;
				_handlePlayerCollision(ref intersection);
			}
		}

		// Ball to block collisions
		for (var i = 0; i < _blockRects.Length; i++)
		{
			if (!_blockStates[i]) continue;

			RectangleF.Intersect(ref _ballRect, ref _blockRects[i], out var intersection);
			if (intersection.IsEmpty) continue;

			_ballSpeed += 3f;
			_blockStates[i] = false;
			_ballVelocity *= _getReboundDirection(ref intersection);
			_ballVelocity = Vector2.Normalize(_ballVelocity);
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
				if (_blockStates[i]) _spriteBatch.Draw(_blockTexture, _blockRects[i], color: _blockColors[i], options: (SpriteEffect)(i % 3));
			}
		}

		_spriteBatch.Draw(_blockTexture, _playerRect, color: Color.DarkBlue);
		_spriteBatch.Draw(_ballTexture, _ballRect, color: Color.DarkRed);

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