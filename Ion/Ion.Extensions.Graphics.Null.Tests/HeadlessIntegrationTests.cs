namespace Ion.Extensions.Graphics.Tests;

/// <summary>
/// A game system written only against the abstractions, as a real game would be.
/// </summary>
public class DrawSomethingSystem(IAssetManager assets, ISpriteBatch spriteBatch, IInputState input, IAudioManager audio)
{
	private ITexture2D _texture = default!;
	private IFont _font = default!;
	private ISoundEffect _sound = default!;

	public List<bool> SpacePressed { get; } = [];

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		_texture = assets.Load<ITexture2D>("rgbt_2x2.png");
		_font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(16);
		_sound = assets.Load<ISoundEffect>("bonk.wav");
		next(dt);
	}

	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		SpacePressed.Add(input.Pressed(Key.Space));
		if (input.Pressed(Key.Space)) audio.Play(_sound, volume: 0.5f);
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		spriteBatch.Draw(_texture, new Vector2(10, 10), new Vector2(_texture.Width, _texture.Height));
		spriteBatch.DrawString(_font, "Score: 0", new Vector2(20, 20));
		next(dt);
	}
}

public class HeadlessIntegrationTests
{
	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AddIonHeadless_RunsAGameWithoutGpuWindowOrAudioDevice()
	{
		using var app = TestApp.CreateHeadless(
			services => services.AddSingleton<DrawSomethingSystem>(),
			app => app.UseSystem<DrawSomethingSystem>());

		Assert.IsType<NullWindow>(app.Services.GetRequiredService<IWindow>());
		Assert.IsType<NullInputState>(app.Services.GetRequiredService<IInputState>());
		Assert.IsType<NullSpriteBatch>(app.Services.GetRequiredService<ISpriteBatch>());
		Assert.IsType<NullAudioManager>(app.Services.GetRequiredService<IAudioManager>());

		var spriteBatch = app.Services.GetRequiredService<NullSpriteBatch>();
		var input = app.Services.GetRequiredService<NullInputState>();
		var audio = app.Services.GetRequiredService<NullAudioManager>();
		var system = app.Services.GetRequiredService<DrawSomethingSystem>();

		var loop = app.Build();
		loop.Init(TestApp.FrameTime);

		loop.Step(TestApp.FrameTime);
		input.Tap(Key.Space);
		loop.Step(TestApp.FrameTime);
		loop.Step(TestApp.FrameTime);

		Assert.Equal(3, spriteBatch.FramesCompleted);

		var frame = spriteBatch.LastFrame;
		Assert.Equal(2, frame.Frame);
		Assert.Equal(1, frame.Sprites);
		Assert.Equal(1, frame.Strings);
		Assert.Equal(2, frame.DrawCalls);

		var sprite = frame.Commands[0];
		Assert.Equal(SpriteBatchCommandKind.Sprite, sprite.Kind);
		Assert.Equal(new Vector2(2, 2), sprite.Size); // the 2x2 PNG, sized from its header
		Assert.Equal("Score: 0", frame.Commands[1].Text);

		Assert.Equal([false, true, false], system.SpacePressed);
		var play = Assert.Single(audio.Plays);
		Assert.Equal(0.5f, play.Volume);
		Assert.Equal("bonk.wav", play.Sound.Name);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData("Ion:Headless", "true", true)]
	[InlineData("Ion:Headless", "false", false)]
	[InlineData("Ion:Graphics:Output", "None", true)]
	[InlineData("Ion:Graphics:Output", "Window", false)]
	public void AddIon_SelectsTheBackendFromConfiguration(string key, string value, bool headless)
	{
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
		var services = new ServiceCollection().AddLogging().AddIon(config);

		Assert.Equal(headless, config.IsHeadless());
		Assert.Equal(headless, services.Any(d => d.ServiceType == typeof(NullWindow)));
		Assert.Equal(headless, services.Any(d => d.ServiceType == typeof(NullAudioManager)));
		Assert.Equal(!headless, services.Any(d => d.ServiceType == typeof(IWindowSurface)));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AddIon_HonorsGraphicsOutputNoneSetInCode()
	{
		var config = new ConfigurationBuilder().Build();
		var services = new ServiceCollection().AddLogging().AddIon(config, graphics => graphics.Output = GraphicsOutput.None);

		Assert.Contains(services, d => d.ServiceType == typeof(NullWindow));
	}
}
