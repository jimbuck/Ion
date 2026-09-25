namespace Ion.Extensions.Graphics.Tests;

public class NullWindowTests
{
	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void Size_ComesFromWindowConfig()
	{
		using var app = TestApp.CreateNullGraphics(new() { ["Ion:Window:Width"] = "320", ["Ion:Window:Height"] = "200", ["Ion:Title"] = "Headless" });

		var window = app.Services.GetRequiredService<IWindow>();

		Assert.IsType<NullWindow>(window);
		Assert.Equal(320u, window.Width);
		Assert.Equal(200u, window.Height);
		Assert.Equal(new Vector2(320, 200), window.Size);
		Assert.Equal("Headless", window.Title);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void Size_DefaultsWhenNotConfigured()
	{
		using var app = TestApp.CreateNullGraphics();

		var window = app.Services.GetRequiredService<NullWindow>();

		Assert.Equal(NullWindow.DefaultWidth, window.Width);
		Assert.Equal(NullWindow.DefaultHeight, window.Height);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void Init_EmitsOneResizeEvent_AndResizingEmitsAnother()
	{
		var resizes = new List<WindowResizeEvent>();

		using var app = TestApp.CreateNullGraphics(
			new() { ["Ion:Window:Width"] = "320", ["Ion:Window:Height"] = "200" },
			use: app => app.UseUpdate((GameLoopDelegate next, IEventListener events) => dt =>
			{
				while (events.On<WindowResizeEvent>(out var e)) resizes.Add(e.Data);
				next(dt);
			}));

		var loop = app.Build();
		loop.Init(TestApp.FrameTime);
		loop.Step(TestApp.FrameTime);
		loop.Step(TestApp.FrameTime);

		Assert.Equal([new WindowResizeEvent(320, 200)], resizes);

		app.Services.GetRequiredService<IWindow>().Size = new Vector2(640, 480);
		loop.Step(TestApp.FrameTime);

		Assert.Equal([new WindowResizeEvent(320, 200), new WindowResizeEvent(640, 480)], resizes);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void Close_EmitsExitGameEvent()
	{
		using var app = TestApp.CreateNullGraphics();
		var window = app.Services.GetRequiredService<NullWindow>();
		var events = app.Services.GetRequiredService<IEventListener>();

		var loop = app.Build();
		loop.Init(TestApp.FrameTime);
		loop.Step(TestApp.FrameTime);
		Assert.False(events.On<ExitGameEvent>());

		window.Close();
		Assert.True(window.IsClosing);
		loop.Step(TestApp.FrameTime);

		Assert.True(events.On<ExitGameEvent>());
		Assert.True(window.IsClosed);
	}

	[Fact(Timeout = 10_000), Trait(CATEGORY, INTEGRATION)]
	public async Task Close_EndsTheGameLoop()
	{
		var frames = 0;
		using var app = TestApp.CreateNullGraphics(use: app => app.UseUpdate((GameLoopDelegate next, NullWindow window) => dt =>
		{
			if (++frames == 3) window.Close();
			next(dt);
		}));

		await Task.Run(app.Run);

		Assert.Equal(3, frames);
		Assert.True(app.Services.GetRequiredService<NullWindow>().IsClosed);
	}
}
