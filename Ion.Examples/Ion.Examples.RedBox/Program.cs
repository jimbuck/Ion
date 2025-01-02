using Microsoft.Extensions.DependencyInjection;
using Ion;
using Ion.Extensions.Graphics;
using System.Numerics;

var builder = IonApplication.CreateBuilder(args);

builder.Services.AddIon(builder.Configuration, graphics =>
{
	graphics.Output = GraphicsOutput.Window;
	graphics.ClearColor = Color.Black;
});

builder.Services.AddSingleton<RedBoxSystem>();

var game = builder.Build();
game.UseIon()
	.UseSystem<RedBoxSystem>();

game.Run();

public class RedBoxSystem
{
	private RectangleF _box;
	private Vector2 _velocity;
	private readonly IInputState _input;
	private readonly IWindow _window;
	private readonly ISpriteBatch _spriteBatch;

	public RedBoxSystem(IInputState input, IWindow window, ISpriteBatch spriteBatch)
	{
		_box = new RectangleF(100, 100, 50, 50);
		_velocity = Vector2.Zero;
		_input = input;
		_window = window;
		_spriteBatch = spriteBatch;
	}

	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		_velocity = Vector2.Zero;

		if (_input.Down(Key.Left)) _velocity.X = -200;
		if (_input.Down(Key.Right)) _velocity.X = 200;
		if (_input.Down(Key.Up)) _velocity.Y = -200;
		if (_input.Down(Key.Down)) _velocity.Y = 200;

		_box.Location += _velocity * dt.Delta;

		_box.X = Math.Clamp(_box.X, 0, _window.Width - _box.Width);
		_box.Y = Math.Clamp(_box.Y, 0, _window.Height - _box.Height);

		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		_spriteBatch.DrawRect(Color.Red, _box);
		next(dt);
	}
}
