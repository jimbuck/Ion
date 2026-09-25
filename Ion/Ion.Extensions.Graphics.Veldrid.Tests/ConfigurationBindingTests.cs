extern alias VeldridGraphics;
extern alias NullGraphics;

using NullBuilder = NullGraphics::Ion.Extensions.Graphics.BuilderExtensions;
using VeldridBuilder = VeldridGraphics::Ion.Extensions.Graphics.BuilderExtensions;

namespace Ion.Extensions.Graphics.Tests;

public class ConfigurationBindingTests
{
	private static IonApplication BuildApp(Action<IServiceCollection, IConfiguration> addGraphics, Dictionary<string, string?> settings)
	{
		var builder = IonApplication.CreateBuilder();
		builder.Configuration.AddInMemoryCollection(settings);
		addGraphics(builder.Services, builder.Configuration);
		return builder.Build();
	}

	private static readonly Dictionary<string, string?> Settings = new()
	{
		["Ion:Window:Width"] = "1234",
		["Ion:Window:Height"] = "567",
		["Ion:Window:ShowCursor"] = "false",
		["Ion:Graphics:PreferredBackend"] = "OpenGL",
		["Ion:Graphics:VSync"] = "true",
		["Ion:Graphics:ClearColorHex"] = "#336699",
	};

	public static TheoryData<string> Backends() => new() { "Null", "Veldrid" };

	private static Action<IServiceCollection, IConfiguration> AddGraphics(string backend) => backend switch
	{
		"Null" => (services, config) => NullBuilder.AddNullGraphics(services, config),
		"Veldrid" => (services, config) => VeldridBuilder.AddVeldridGraphics(services, config),
		_ => throw new ArgumentOutOfRangeException(nameof(backend)),
	};

	[Theory, Trait(CATEGORY, UNIT)]
	[MemberData(nameof(Backends))]
	public void WindowConfig_IsBoundFromIonWindowSection(string backend)
	{
		using var app = BuildApp(AddGraphics(backend), Settings);

		var window = app.Services.GetRequiredService<IOptions<WindowConfig>>().Value;

		Assert.Equal(1234, window.Width);
		Assert.Equal(567, window.Height);
		Assert.False(window.ShowCursor);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[MemberData(nameof(Backends))]
	public void GraphicsConfig_IsBoundFromIonGraphicsSection(string backend)
	{
		using var app = BuildApp(AddGraphics(backend), Settings);

		var graphics = app.Services.GetRequiredService<IOptions<GraphicsConfig>>().Value;

		Assert.Equal(GraphicsBackend.OpenGL, graphics.PreferredBackend);
		Assert.True(graphics.VSync);
		Assert.Equal(new Color(0x33, 0x66, 0x99, 0xFF), graphics.ClearColor);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[MemberData(nameof(Backends))]
	public void WindowConfig_ResolvesWithDefaultsWhenSectionIsMissing(string backend)
	{
		using var app = BuildApp(AddGraphics(backend), []);

		var window = app.Services.GetRequiredService<IOptions<WindowConfig>>().Value;

		Assert.Null(window.Width);
		Assert.True(window.ShowCursor);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SectionOverload_StillBindsGraphicsConfig()
	{
		using var app = BuildApp((services, config) => NullBuilder.AddNullGraphics(services, config.GetSection("Ion:Graphics")), Settings);

		Assert.True(app.Services.GetRequiredService<IOptions<GraphicsConfig>>().Value.VSync);
		Assert.Null(app.Services.GetRequiredService<IOptions<WindowConfig>>().Value.Width);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ConfigureOptions_RunsAfterConfigurationBinding()
	{
		using var app = BuildApp((services, config) => VeldridBuilder.AddVeldridGraphics(services, config, g => g.ClearColor = Color.CornflowerBlue), Settings);

		Assert.Equal(Color.CornflowerBlue, app.Services.GetRequiredService<IOptions<GraphicsConfig>>().Value.ClearColor);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData("#333", 0x33, 0x33, 0x33, 0xFF)]
	[InlineData("123a", 0x11, 0x22, 0x33, 0xAA)]
	[InlineData("#336699", 0x33, 0x66, 0x99, 0xFF)]
	[InlineData("33669980", 0x33, 0x66, 0x99, 0x80)]
	public void ClearColorHex_ParsesSupportedForms(string hex, int r, int g, int b, int a)
	{
		var config = new GraphicsConfig { ClearColorHex = hex };

		Assert.Equal(new Color(r, g, b, a), config.ClearColor);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData("#12")]
	[InlineData("#GGGGGG")]
	[InlineData("#1234567")]
	public void ClearColorHex_RejectsInvalidValues(string hex)
	{
		Assert.Throws<FormatException>(() => new GraphicsConfig { ClearColorHex = hex });
	}
}
