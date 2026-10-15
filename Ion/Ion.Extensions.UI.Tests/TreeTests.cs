using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Graphics;
using Ion.Testing;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.UI.Tests;

public class TreeTests
{
	private static readonly string[] Items = ["Easy", "Normal", "Hard"];

	private sealed class Screen
	{
		public bool Music = true;
		public float Volume = 0.5f;
		public int Difficulty = 1;
		public string Name = "Ion";
		public int Score;

		public void Build(Ui ui)
		{
			using (ui.Panel("options"))
			{
				ui.Label("Options", scale: 2);
				Span<char> buffer = stackalloc char[16];
				Score.TryFormat(buffer, out var written);
				ui.Label(buffer[..written], "score");
				ui.Toggle("Music", ref Music);
				ui.Slider("Volume", ref Volume, 0, 1, 0.05f);
				ui.List("Difficulty", Items, ref Difficulty);
				ui.TextInput("Name", ref Name);
				using (ui.Row("buttons"))
				{
					ui.Button("OK");
					ui.Button("OK");
					ui.Button("Cancel");
				}
			}
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NodesArePublishedInPreOrderWithPathsKindsAndValues()
	{
		var h = new UiHarness();
		var screen = new Screen();
		h.Frame(screen.Build);

		var nodes = h.Tree.Snapshot();
		Assert.Equal(
		[
			"options", "options/label", "options/score", "options/Music", "options/Volume", "options/Difficulty",
			"options/Difficulty/Easy", "options/Difficulty/Normal", "options/Difficulty/Hard", "options/Name",
			"options/buttons", "options/buttons/OK", "options/buttons/OK#2", "options/buttons/Cancel",
		], nodes.Select(n => n.Path));
		Assert.Equal(nodes.Length, h.Tree.Count);

		var volume = h.Node("options/Volume");
		Assert.Equal(UiNodeKind.Slider, volume.Kind);
		Assert.Equal("Volume", volume.Text);
		Assert.Equal("0.5", volume.Value);
		Assert.Equal(1, volume.Depth);
		Assert.Equal(0, volume.Parent);
		Assert.True(volume.Focusable);
		Assert.Equal("true", h.Node("options/Music").Value);
		Assert.Equal("Normal", h.Node("options/Difficulty").Value);
		Assert.Equal("Ion", h.Node("options/Name").Value);
		Assert.Equal("0", h.Node("options/score").Text);
		Assert.Equal(UiNodeKind.Panel, nodes[0].Kind);
		Assert.Equal(-1, nodes[0].Parent);
		Assert.Equal(5, h.Node("options/Difficulty/Hard").Parent);
		Assert.Equal("options/Music", h.Tree.FocusedPath);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheTreeIsStableAcrossFrames()
	{
		var h = new UiHarness();
		var screen = new Screen();
		h.Frame(screen.Build);
		var first = h.Tree.Snapshot();
		var version = h.Tree.Version;

		for (var i = 0; i < 10; i++)
		{
			screen.Score += 10;
			h.Frame(screen.Build);
		}

		var last = h.Tree.Snapshot();
		Assert.Equal(version, h.Tree.Version);
		Assert.Equal(11, h.Tree.Frame);
		Assert.Equal(first.Length, last.Length);
		for (var i = 0; i < first.Length; i++)
		{
			// The same string instances: paths are cached per widget, not rebuilt.
			Assert.Same(first[i].Path, last[i].Path);
			Assert.Equal(first[i].Rect, last[i].Rect);
		}

		Assert.Equal("100", h.Node("options/score").Text);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AChangeOfStructureBumpsTheVersion()
	{
		var h = new UiHarness();
		var extra = false;
		void Build(Ui ui)
		{
			ui.Button("A");
			if (extra) ui.Button("B");
		}

		h.Frame(Build);
		var version = h.Tree.Version;
		extra = true;
		h.Frame(Build);
		Assert.Equal(version + 1, h.Tree.Version);
		h.Frame(Build);
		Assert.Equal(version + 1, h.Tree.Version);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WidgetsOfOneCallSiteInALoopKeepTheirOwnState()
	{
		var h = new UiHarness();
		var values = new bool[3];
		void Build(Ui ui)
		{
			for (var i = 0; i < values.Length; i++) ui.Toggle("Flag", ref values[i]);
		}

		h.Frame(Build);
		Assert.True(h.Tree.Click("Flag#2"));
		h.Frame(Build);
		Assert.Equal([false, true, false], values);
		Assert.Equal("true", h.Node("Flag#2").Value);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void CommandsAreCheckedAgainstTheTreeAndAppliedNextFrame()
	{
		var h = new UiHarness();
		var screen = new Screen();
		h.Frame(screen.Build);

		Assert.False(h.Tree.Click("options/Nope"));
		Assert.False(h.Tree.Click("options/label"));
		Assert.False(h.Tree.SetValue("options/Music", "maybe"));
		Assert.False(h.Tree.SetValue("options/Volume", "loud"));
		Assert.False(h.Tree.Focus("options/buttons"));

		Assert.True(h.Tree.Click("options/Music"));
		Assert.True(h.Tree.SetValue("options/Volume", "0.73"));
		Assert.True(h.Tree.SetValue("options/Difficulty", "Hard"));
		Assert.True(h.Tree.Focus("options/buttons/Cancel"));

		// Nothing happens until the next frame.
		Assert.True(screen.Music);
		h.Frame(screen.Build);
		Assert.False(screen.Music);
		Assert.Equal(0.75f, screen.Volume, 5);
		Assert.Equal(2, screen.Difficulty);
		Assert.Equal("options/buttons/Cancel", h.Tree.FocusedPath);
		Assert.Equal("0.75", h.Node("options/Volume").Value);

		Assert.True(h.Tree.SetValue("options/Difficulty", "0"));
		Assert.True(h.Tree.SetValue("options/Music", "true"));
		h.Frame(screen.Build);
		Assert.Equal(0, screen.Difficulty);
		Assert.True(screen.Music);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ClickRoundTripsThroughTheRunningGame()
	{
		using var host = new IonTestHost()
			.Configure(services => services.AddUi())
			.ConfigureApp(app => app.UseUi())
			.WithSystem<CounterSystem>();

		host.Step();
		var tree = host.Get<IUiTree>();
		Assert.Same(host.Get<Ui>().Tree, tree);
		Assert.True(tree.TryFind("counter/Add", out var add));
		Assert.Equal(UiNodeKind.Button, add.Kind);
		Assert.Equal("0", tree.TryFind("counter/count", out var count) ? count.Text : null);

		Assert.True(tree.Click("counter/Add"));
		Assert.True(tree.Click("counter/Add"));
		host.Step();
		// Two clicks queued for one frame click the button once.
		Assert.Equal(1, host.Get<CounterSystem>().Count);
		Assert.True(tree.Click("counter/Add"));
		host.Step();
		Assert.Equal(2, host.Get<CounterSystem>().Count);
		host.Step();
		Assert.True(tree.TryFind("counter/count", out count));
		Assert.Equal("2", count.Text);
		Assert.Equal("counter/Add", tree.FocusedPath);

		// Drawn through the sprite batch at StageOrder.Ui: the panel, the button and the focus outline.
		Assert.True(host.SpriteBatch.LastFrame.Rects >= 6);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void KeyboardInputReachesTheUiThroughTheEngine()
	{
		using var host = new IonTestHost()
			.Configure(services => services.AddUi())
			.ConfigureApp(app => app.UseUi())
			.WithSystem<CounterSystem>();

		host.Step();
		host.Input.Tap(Key.Enter);
		host.Step();
		Assert.Equal(1, host.Get<CounterSystem>().Count);
	}

	public sealed class CounterSystem(Ui ui)
	{
		public int Count;

		[Update]
		public void Build(GameTime dt)
		{
			using (ui.Panel("counter"))
			{
				Span<char> text = stackalloc char[12];
				Count.TryFormat(text, out var written);
				ui.Label(text[..written], "count");
				if (ui.Button("Add")) Count++;
			}
		}
	}
}
