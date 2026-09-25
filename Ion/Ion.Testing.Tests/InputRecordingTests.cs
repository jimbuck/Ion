using System.Text;

using Ion.Extensions.Graphics;
using Ion.Testing;

namespace Ion.Tests;

/// <summary>
/// Logs, every Update and every fixed step, what the game sees of the input.
/// </summary>
public sealed class InputLogSystem(IInputState input)
{
	private static readonly Key[] Keys = [Key.A, Key.S, Key.D, Key.W, Key.Space, Key.Enter, Key.ShiftLeft];
	private static readonly MouseButton[] Buttons = [MouseButton.Left, MouseButton.Right];
	private static readonly GamepadButton[] PadButtons = [GamepadButton.A, GamepadButton.B, GamepadButton.Start];

	public List<string> Frames { get; } = [];
	public List<string> FixedSteps { get; } = [];

	[FixedUpdate]
	public void FixedUpdate(GameTime dt) => FixedSteps.Add(Describe());

	[Update]
	public void Update(GameTime dt) => Frames.Add($"{dt.Frame}: {Describe()}");

	private string Describe()
	{
		var sb = new StringBuilder();
		foreach (var key in Keys) sb.Append(input.Pressed(key) ? 'P' : '-').Append(input.Down(key) ? 'D' : '-').Append(input.Released(key) ? 'R' : '-').Append(' ');
		sb.Append(input.Pressed(Key.S, ModifierKeys.Shift) ? "shift+S " : "");
		foreach (var button in Buttons) sb.Append(input.Pressed(button) ? 'P' : '-').Append(input.Down(button) ? 'D' : '-').Append(' ');
		sb.Append($"mouse {input.MousePosition} d{input.MouseDelta} wheel {input.WheelDelta} text '{input.Text}' ");
		foreach (var pad in input.Gamepads)
		{
			sb.Append($"pad{pad.Index} ");
			foreach (var button in PadButtons) sb.Append(pad.Pressed(button) ? 'P' : '-').Append(pad.Down(button) ? 'D' : '-');
			sb.Append($" {pad.LeftStick} ");
		}

		return sb.ToString();
	}
}

public class InputRecordingTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"ion-input-{Guid.NewGuid():N}.ioni");

	public void Dispose()
	{
		if (File.Exists(_path)) File.Delete(_path);
	}

	private static void ScriptRandomInput(NullInputState input, Random random)
	{
		Key[] keys = [Key.A, Key.S, Key.D, Key.W, Key.Space, Key.Enter, Key.ShiftLeft];
		switch (random.Next(10))
		{
			case 0: input.Press(keys[random.Next(keys.Length)], random.Next(2) == 0 ? ModifierKeys.Shift : ModifierKeys.None); break;
			case 1: input.Release(keys[random.Next(keys.Length)]); break;
			case 2: input.Tap(keys[random.Next(keys.Length)], ModifierKeys.Shift); break;
			case 3: input.Click(random.Next(2) == 0 ? MouseButton.Left : MouseButton.Right); break;
			case 4: input.SetMousePosition(random.Next(640), random.Next(480)); break;
			case 5: input.Scroll(random.Next(-3, 4)); break;
			case 6: input.Type(random.Next(2) == 0 ? "ab" : "Z"); break;
			case 7: input.Tap(random.Next(2), GamepadButton.A); break;
			case 8: input.SetLeftStick(0, new Vector2(random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1)); break;
			default: break; // a frame without input
		}

		if (random.Next(20) == 0) input.ReleaseAll();
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void RecordThenReplayProducesIdenticalInputOver100Frames()
	{
		const int frames = 100;
		List<string> recordedFrames, recordedFixed;
		long recordedEvents;

		using (var host = new IonTestHost().WithSystem<InputLogSystem>().Configure(s => s.AddInputRecording(_path)))
		{
			var random = new Random(1234);
			for (var i = 0; i < frames; i++)
			{
				ScriptRandomInput(host.Input, random);
				host.Step();
			}

			var log = host.Get<InputLogSystem>();
			recordedFrames = [.. log.Frames];
			recordedFixed = [.. log.FixedSteps];
			recordedEvents = host.Get<InputRecorder>().EventCount;
		}

		Assert.True(recordedEvents > 50, $"Only {recordedEvents} events were recorded.");
		Assert.Equal(frames, recordedFrames.Count);
		Assert.Contains(recordedFrames, f => f.Contains("PD-"));
		Assert.Contains(recordedFrames, f => f.Contains("pad0"));

		using (var host = new IonTestHost().WithSystem<InputLogSystem>().Configure(s => s.AddInputPlayback(_path)))
		{
			// Scripted input is ignored while the recording plays.
			var noise = new Random(99);
			for (var i = 0; i < frames; i++)
			{
				ScriptRandomInput(host.Input, noise);
				host.Step();
			}

			var log = host.Get<InputLogSystem>();
			Assert.Equal(recordedFrames, log.Frames);
			Assert.Equal(recordedFixed, log.FixedSteps);
			Assert.True(host.Get<InputPlayer>().IsPlaying);

			// After the recording ends, scripted input works again.
			host.Step();
			Assert.False(host.Get<InputPlayer>().IsPlaying);
			host.Input.Press(Key.W);
			host.Step();
			Assert.True(host.Input.Pressed(Key.W));
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RecordingRoundTripsEveryEventKind()
	{
		using var stream = new MemoryStream();
		var tracker = new InputTracker();
		using (var recorder = new InputRecorder(stream, leaveOpen: true))
		{
			tracker.Recorder = recorder;
			tracker.BeginFrame();
			tracker.OnKey(Key.Q, true, false, ModifierKeys.Control | ModifierKeys.Gui);
			tracker.OnKey(Key.Q, true, true, ModifierKeys.None);
			tracker.OnMouseButton(MouseButton.Middle, true);
			tracker.OnMouseMove(new Vector2(1.5f, -2.25f));
			tracker.OnWheel(-1.5f);
			tracker.OnText('中');
			tracker.BeginFrame();
			tracker.BeginFrame();
			tracker.OnGamepadButton(3, GamepadButton.DPadLeft, true);
			tracker.OnGamepadAxis(3, GamepadAxis.RightY, -0.75f);
			tracker.OnGamepadConnected(3, false);
			tracker.ReleaseAll();
			tracker.BeginFrame();
		}

		stream.Position = 0;
		var player = new InputPlayer(stream);
		Assert.Equal(3u, player.LastFrame);
		Assert.Equal(
		[
			(0u, InputEvent.ForKey(Key.Q, true, false, ModifierKeys.Control | ModifierKeys.Gui)),
			(0u, InputEvent.ForKey(Key.Q, true, true)),
			(0u, InputEvent.ForMouseButton(MouseButton.Middle, true)),
			(0u, InputEvent.ForMouseMove(new Vector2(1.5f, -2.25f))),
			(0u, InputEvent.ForWheel(-1.5f)),
			(0u, InputEvent.ForText('中')),
			(2u, InputEvent.ForGamepadConnection(3, true)),
			(2u, InputEvent.ForGamepadButton(3, GamepadButton.DPadLeft, true)),
			(2u, InputEvent.ForGamepadAxis(3, GamepadAxis.RightY, -0.75f)),
			(2u, InputEvent.ForGamepadConnection(3, false)),
			(2u, InputEvent.ForReleaseAll()),
		], player.Events.ToList());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void PlayerRejectsOtherFiles()
	{
		Assert.Throws<InvalidDataException>(() => new InputPlayer(new MemoryStream("nope!"u8.ToArray())));
		Assert.Throws<InvalidDataException>(() => new InputPlayer(new MemoryStream([.. "IONI"u8, 99])));
	}
}
