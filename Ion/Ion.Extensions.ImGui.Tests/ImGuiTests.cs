using System.Text;

using Microsoft.Extensions.DependencyInjection;

using Hexa.NET.ImGui;

using Ion.Extensions.Graphics;
using Ion.Testing;

using static Ion.Tests.TestConstants;

using DearImGui = Hexa.NET.ImGui.ImGui;

namespace Ion.Extensions.ImGui.Tests;

/// <summary>Dear ImGui's current context is process-wide: the tests of this assembly run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ImGuiCollection
{
	public const string Name = "ImGui";
}

/// <summary>What the test's Update step draws with ImGui.</summary>
public sealed class ImGuiScript
{
	public Action? Update { get; set; }
	public Action<ISpriteBatch>? Render { get; set; }
}

public sealed class ImGuiScriptSystem(ImGuiScript script, IServiceProvider services)
{
	[Update]
	public void Update(GameTime dt) => script.Update?.Invoke();

	[Render]
	public void Render(GameTime dt)
	{
		if (script.Render is { } render) render(services.GetRequiredService<ISpriteBatch>());
	}
}

[Collection(ImGuiCollection.Name), Trait(CATEGORY, INTEGRATION)]
public sealed unsafe class ImGuiTests
{
	private static IonTestHost Host(ImGuiScript script, Action<ImGuiOptions>? configure = null) => new IonTestHost()
		.Configure(services => services.AddImGui(configure: configure).AddSingleton(script))
		.ConfigureApp(app => app.UseImGui())
		.WithSystem<ImGuiScriptSystem>();

	/// <summary>A window at (20, 20) with one button; records the button's rectangle and its clicks.</summary>
	private sealed class ButtonWindow
	{
		public Vector2 Min, Max;
		public int Clicks;
		public bool Hovered;

		public void Draw()
		{
			DearImGui.SetNextWindowPos(new Vector2(20, 20));
			DearImGui.SetNextWindowSize(new Vector2(200, 120));
			DearImGui.Begin("Tools");
			if (DearImGui.Button("Spawn")) Clicks++;
			Min = DearImGui.GetItemRectMin();
			Max = DearImGui.GetItemRectMax();
			Hovered = DearImGui.IsWindowHovered();
			DearImGui.End();
		}
	}

	[Fact]
	public void FramesRunHeadlessWithoutAGpu()
	{
		var window = new ButtonWindow();
		using var host = Host(new ImGuiScript { Update = window.Draw });
		host.Step(3);

		var imgui = host.Get<ImGuiHost>();
		Assert.False(imgui.IsRendering);
		Assert.False(imgui.IsFrameOpen);
		Assert.Equal(3, imgui.FrameCount);
		Assert.True(window.Max.X > window.Min.X && window.Min.X >= 20, $"button at {window.Min}..{window.Max}");
	}

	[Fact]
	public void AClickThroughIonsInputPressesAButtonAndCapturesTheMouse()
	{
		var window = new ButtonWindow();
		using var host = Host(new ImGuiScript { Update = window.Draw });
		host.Step();
		var imgui = host.Get<ImGuiHost>();
		Assert.False(imgui.WantCaptureMouse);

		host.Input.SetMousePosition((window.Min + window.Max) / 2);
		host.Step(2);
		Assert.True(window.Hovered);
		Assert.True(imgui.WantCaptureMouse);

		// A press and a release within one frame: ImGui still sees both.
		host.Input.Click();
		host.Step(4);
		Assert.Equal(1, window.Clicks);

		host.Input.SetMousePosition(new Vector2(600, 400));
		host.Step(2);
		Assert.False(imgui.WantCaptureMouse);
	}

	[Fact]
	public void TypedTextReachesAFocusedTextField()
	{
		var buffer = new byte[64];
		var focus = true;
		using var host = Host(new ImGuiScript
		{
			Update = () =>
			{
				DearImGui.SetNextWindowPos(new Vector2(20, 20));
				DearImGui.Begin("Name");
				if (focus) DearImGui.SetKeyboardFocusHere();
				focus = false;
				fixed (byte* text = buffer) DearImGui.InputText("##name", text, (nuint)buffer.Length);
				DearImGui.End();
			},
		});
		host.Step(2);

		host.Input.Type("Ion é");
		host.Step(3);
		Assert.Equal("Ion é", Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0)));
		var imgui = host.Get<ImGuiHost>();
		Assert.True(imgui.WantCaptureKeyboard);
		Assert.True(imgui.WantTextInput);

		host.Input.Tap(Key.BackSpace);
		host.Step(3);
		Assert.Equal("Ion ", Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0)));
	}

	[Fact]
	public void DisabledImGuiRunsItsFramesButIgnoresInput()
	{
		var window = new ButtonWindow();
		using var host = Host(new ImGuiScript { Update = window.Draw }, options => options.Enabled = false);
		host.Step();
		host.Input.SetMousePosition((window.Min + window.Max) / 2);
		host.Step();
		host.Input.Click();
		host.Step(4);

		Assert.Equal(0, window.Clicks);
		Assert.False(host.Get<ImGuiHost>().WantCaptureMouse);
		Assert.Equal(6, host.Get<ImGuiHost>().FrameCount);
	}

	public static TheoryData<GraphicsBackend> Backends()
	{
		var data = new TheoryData<GraphicsBackend>();
		if (RenderingEnvironment.HasVulkan) data.Add(GraphicsBackend.Vulkan);
		if (RenderingEnvironment.HasHeadlessGles) data.Add(GraphicsBackend.OpenGLES);
		return data;
	}

	[Theory, Trait(CATEGORY, E2E)]
	[MemberData(nameof(Backends))]
	public void ImGuiIsDrawnOnTopOfTheSprites(GraphicsBackend backend)
	{
		var log = new ErrorLog();
		var window = new ButtonWindow();
		Screenshot shot;
		using (var host = log.Attach(Host(new ImGuiScript
		{
			Update = () =>
			{
				window.Draw();
				// A green square drawn by ImGui's draw list over the window, in pixels.
				DearImGui.GetForegroundDrawList().AddRectFilled(new Vector2(150, 150), new Vector2(190, 190), 0xFF00FF00);
			},
			Render = batch => batch.DrawRect(Color.Red, new RectangleF(0, 0, 320, 240)),
		}))
			.WithRendering(320, 240)
			.WithConfiguration("Ion:Graphics:PreferredBackend", backend.ToString()))
		{
			host.Step(3);
			Assert.Equal(backend, host.Get<IGraphicsFrame>().Device.Backend);
			var imgui = host.Get<ImGuiHost>();
			Assert.True(imgui.IsRendering);
			Assert.True(imgui.LastDrawCalls >= 2, $"{imgui.LastDrawCalls} draw calls");
			shot = host.Screenshot();
		}

		log.AssertClean();
		// The sprite everywhere ImGui draws nothing, ImGui's square over it, and the window's (dark) background.
		Assert.Equal(new Rgba8(255, 0, 0, 255), shot.GetPixel(300, 220));
		Assert.Equal(new Rgba8(0, 255, 0, 255), shot.GetPixel(170, 170));
		var background = shot.GetPixel(120, 120);
		Assert.True(background.R < 64 && background.G < 64 && background.B < 64, $"window background {background}");
		// The button's face (ImGui's blue).
		var button = shot.GetPixel((int)((window.Min.X + window.Max.X) / 2), (int)window.Min.Y + 2);
		Assert.True(button.B > button.R, $"button {button}");
	}
}
