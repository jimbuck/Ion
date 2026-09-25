extern alias VeldridGraphics;

using VeldridGraphics::Ion.Extensions.Graphics;

using VeldridBuilder = VeldridGraphics::Ion.Extensions.Graphics.BuilderExtensions;
using Window = VeldridGraphics::Ion.Extensions.Graphics.Window;
using VeldridInputState = VeldridGraphics::Ion.InputState;

namespace Ion.Extensions.Graphics.Tests;

public class RegistrationTests
{
	private sealed class FakeWindow : IWindow
	{
		public uint Width { get; set; } = 1;
		public uint Height { get; set; } = 1;
		public System.Numerics.Vector2 Size { get; set; }
		public bool IsClosing => false;
		public bool IsClosed => false;
		public bool IsActive => true;
		public bool IsVisible { get; set; }
		public bool IsMaximized { get; set; }
		public bool IsMinimized { get; set; }
		public bool IsFullscreen { get; set; }
		public bool IsBorderless { get; set; }
		public bool IsMouseGrabbed { get; set; }
		public string Title { get; set; } = "";
		public bool IsResizable { get; set; }
		public bool IsCursorVisible { get; set; }
	}

	private static IonApplication BuildApp(Action<IServiceCollection>? configure = null)
	{
		var builder = IonApplication.CreateBuilder();
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Ion:Graphics:Output"] = "None" });
		VeldridBuilder.AddVeldridGraphics(builder.Services, builder.Configuration);
		configure?.Invoke(builder.Services);
		return builder.Build();
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Interfaces_ResolveToTheConcreteSingletons()
	{
		using var app = BuildApp();
		var services = app.Services;

		Assert.Same(services.GetRequiredService<Window>(), services.GetRequiredService<IWindow>());
		Assert.Same(services.GetRequiredService<GraphicsContext>(), services.GetRequiredService<IGraphicsContext>());
		Assert.Same(services.GetRequiredService<SpriteBatch>(), services.GetRequiredService<ISpriteBatch>());
		Assert.Same(services.GetRequiredService<VeldridInputState>(), services.GetRequiredService<IInputState>());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EngineSystems_DoNotDependOnTheInterfaceRegistrations()
	{
		var fake = new FakeWindow();
		using var app = BuildApp(services => services.AddSingleton<IWindow>(fake));
		var services = app.Services;

		// Games see the fake; the engine systems keep using the Veldrid window without casting the interface.
		Assert.Same(fake, services.GetRequiredService<IWindow>());
		Assert.NotNull(services.GetRequiredService<WindowSystem>());
		Assert.NotNull(services.GetRequiredService<InputSystem>());
		Assert.NotNull(services.GetRequiredService<GraphicsSystem>());
		Assert.NotNull(services.GetRequiredService<SpriteBatchSystem>());
	}
}
