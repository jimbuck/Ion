---
title: Recording and playback
description: Record every frame's input to a file, replay it deterministically as a test, and inject input from code, tools and agents.
sidebar:
  order: 5
---

Ion can write every input event the game receives to a compact binary file, tagged with its frame number, and replay
that file later in place of the devices. Combined with a deterministic clock, a replayed session reproduces the same
`Pressed`/`Down` sequence frame by frame, which turns a play session into a regression test. The same event path also
lets code, tools and agents inject input into a running game.

| Builder call | What it does |
|---|---|
| `builder.AddInputRecording(path)` | Records every frame's input to `path` (completed when the application is disposed). |
| `builder.AddInputPlayback(path)` | Replays the recording at `path` from the first frame, replacing device and scripted input until it ends. |
| `builder.AddScriptedInput()` | Registers `ScriptedInput`, a thread-safe queue of events applied at the start of the next frame, alongside the devices. |

All three work with both input backends (windowed and headless), because both build on the shared `InputTracker`. The
service collection forms (`services.AddInputRecording(path)`, `AddInputPlayback(path)`, `AddScriptedInput()`) do the
same.

## Recording a session

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon().AddSystem<PlayerSystem>();

// dotnet run -- --record=session.ioni
if (builder.Configuration["record"] is { Length: > 0 } recordPath) builder.AddInputRecording(recordPath);

using var game = builder.Build();
game.UseIon().UseSystem<PlayerSystem>();
game.Run();
```

`AddInputRecording` registers an `InputRecorder` and attaches it to the tracker. Every event the tracker applies, from
the device, from `ScriptedInput` or from a playback, is passed to the recorder at the input step of its frame. The file
is created (with its directory) when the input tracker is created at startup, and completed when the application is disposed: `using var
game` does that at the end of `Run`. `InputRecorder.EventCount` tells you how many events were recorded.

:::caution[Dispose the application]
The end marker, which tells playback how many trailing frames without input to cover, is written on dispose. A process
that is killed leaves a file that plays back fine but ends at its last event. Call `InputRecorder.Flush()` to write
what has been recorded so far.
:::

## Replaying it

```csharp
if (builder.Configuration["replay"] is { Length: > 0 } replayPath) builder.AddInputPlayback(replayPath);
```

`AddInputPlayback` registers an `InputPlayer` that loads the whole file at startup and attaches it to the tracker as
its playback. From the first frame:

- At each frame's input step, the events recorded for that frame number (and any earlier ones not applied yet) are
  applied, exactly as the device produced them.
- Device events and scripted input are ignored while the recording plays.
- `InputPlayer.IsPlaying` turns false after the last recorded frame; from then on the devices and scripted input work
  again.

`InputPlayer` also exposes `LastFrame`, `EventCount`, the decoded `Events` as `(Frame, InputEvent)` pairs, and
`Rewind()` to start again. Loading a file that is not a recording, or of another format version, throws
`InvalidDataException`.

## Determinism

A recording stores frame numbers, not times. Replaying it reproduces the input of each frame exactly. For the whole
game to behave the same, the rest of the frame must be deterministic too:

- **Use a fixed clock.** `IonTestHost` runs on a `FixedStepClock` (one 60 Hz fixed step per frame by default), and so
  does a headless `ion run` with a frame count (`Ion:Run:FixedStep`). Then every frame has the same delta and runs the
  same number of fixed steps, so the fixed-step view of input (see
  [Input overview](/Ion/interaction/input/overview/#edges-in-update-and-fixedupdate)) matches as well.
- **Seed your randomness** from configuration (for example `Ion:Seed`) rather than the time.
- **A windowed recording** replayed windowed gets the same input per frame, but real frame times vary, so time-based
  movement and the distribution of fixed steps can drift. Replay windowed recordings headless under a fixed clock for
  exact reproduction.

See [Time and determinism](/Ion/concepts/time-and-determinism/).

## Replay as a test

This is the pattern of Ion's own test, which records 100 frames of random input and checks that replaying it gives an
identical log of what the game saw, in Update and in every fixed step:

```csharp title="InputReplayTests.cs"
using Ion;
using Ion.Testing;
using Xunit;

public class InputReplayTests
{
	[Fact]
	public void ARecordedSessionReplaysIdentically()
	{
		var path = Path.Combine(Path.GetTempPath(), $"session-{Guid.NewGuid():N}.ioni");
		float recordedX;

		using (var host = new IonTestHost().WithSystem<PlayerSystem>().Configure(s => s.AddInputRecording(path)))
		{
			host.Step();
			host.Input.Press(Key.D);
			host.Step(30);
			host.Input.Release(Key.D);
			host.Input.Tap(Key.Space);
			host.Step(10);
			recordedX = host.Get<PlayerSystem>().Position.X;
		}   // disposing the host completes the recording

		using (var host = new IonTestHost().WithSystem<PlayerSystem>().Configure(s => s.AddInputPlayback(path)))
		{
			host.Step(42);
			Assert.Equal(recordedX, host.Get<PlayerSystem>().Position.X);
		}

		File.Delete(path);
	}
}
```

You can also commit a recording of a real bug report next to a test and replay it with `AddInputPlayback`. To test a
game's own `Program.cs`, use `IonTestHost.UseEntryPoint<Program>()` and add playback with `Configure`, or pass
arguments your program reads with `WithArgs`. See [Testing](/Ion/tooling/testing/).

## The file format

The format (`InputRecordingFormat`, extension `.ioni` by convention) is little-endian:

- A header: the four ASCII bytes `IONI` and a version byte (currently 1).
- Frame blocks: the frame number and the event count (both 7-bit encoded), then the events. Frames without events are
  not written.
- A final block with an event count of zero, giving the last recorded frame.

Each event is its `InputEventKind` byte (`Key`, `MouseButton`, `MouseMove`, `Wheel`, `Text`, `GamepadConnection`,
`GamepadButton`, `GamepadAxis`, `ReleaseAll`, `Touch`) and a small payload. Touch events were added without a version
change: a recording without touches is byte-identical to one from before, and older readers reject only recordings
that contain touches.

## Using the recorder and player directly

`InputRecorder` and `InputPlayer` are ordinary classes over a file or any `Stream`:

```csharp
// Record into memory.
using var stream = new MemoryStream();
var tracker = new InputTracker();
using (var recorder = new InputRecorder(stream, leaveOpen: true))
{
	tracker.Recorder = recorder;
	tracker.BeginFrame();
	tracker.OnKey(Key.Q, true, false, ModifierKeys.Control);
	tracker.BeginFrame();
}

// Inspect it.
stream.Position = 0;
var player = new InputPlayer(stream);
foreach (var (frame, e) in player.Events) Console.WriteLine($"{frame}: {e.Kind}");

// Replay into any IInputEventSink.
player.Rewind();
player.Play(frame: 0, sink: tracker);
```

The pieces fit together through small interfaces in `Ion.Core.Abstractions`:

| Type | Role |
|---|---|
| `IInputEventSink` | Receives raw events in order (`OnKey`, `OnMouseButton`, `OnMouseMove`, `OnWheel`, `OnText`, `OnGamepadConnected`, `OnGamepadButton`, `OnGamepadAxis`, `OnTouch`, `ReleaseAll`). `InputTracker` implements it. |
| `IInputRecorder` | A sink that also gets `BeginFrame(frame)`; set as `InputTracker.Recorder`. |
| `IInputPlayback` | Supplies each frame's events instead of the device; set as `InputTracker.Playback`. |
| `IInputScript` | Supplies injected events in addition to the device; set as `InputTracker.Script`. |
| `IInputTrackerHook` | Registered as a singleton, attached to the tracker when it is created. The `Add...` methods register their recorder, player or script this way. |

## Scripted input

`ScriptedInput` injects events into a live game from any thread. Events go through the tracker's normal path, so they
produce the same edges, fixed-step views and recordings as a device. Register it with `builder.AddScriptedInput()` and
inject it:

```csharp
public sealed class DemoSystem(ScriptedInput script)
{
	[Init]
	public void Init()
	{
		script.Tap(Key.Enter);                            // next frame: press and release
		script.Hold(Key.Right, frames: 60);               // hold for a second at 60 FPS
		script.Click(new Vector2(400, 300));              // move the pointer and click there
		script.Type("Ada");                               // text input
		script.Enqueue(InputEvent.ForGamepadButton(0, GamepadButton.A, true), delayFrames: 90);
		script.Enqueue(InputEvent.ForGamepadButton(0, GamepadButton.A, false), delayFrames: 91);
	}
}
```

| Member | Meaning |
|---|---|
| `Enqueue(e, delayFrames = 0)` | Queue any `InputEvent` for the next frame, or `delayFrames` frames after it. Events due in the same frame apply in queue order. |
| `Tap(key, modifiers)` | Press and release within the next frame. |
| `Hold(key, frames, modifiers)` | Press now, release `frames` frames later (at least 1). |
| `Click(position, button = Left)` | Move the pointer and click there. |
| `Type(text)` | Every character as text input. |
| `Clear()` | Drop pending events. |
| `PendingCount`, `AppliedCount` | Queue statistics. |

Scripted events are applied after a playback, alongside device input, and are ignored while a playback is playing.
The [Companion](/Ion/examples/companion/) sample uses `ScriptedInput` to turn phones on a WebSocket into virtual
gamepads.

:::tip[Scripted input in tests]
In tests, `IonTestHost.Input` (the headless `NullInputState`) is usually simpler: `Press`, `Tap`, `Click`, `Type`,
`SetLeftStick` and `TouchTap` are queued for the next frame in the same way. `ScriptedInput` is the one to use when
the input has to reach a windowed game or come from another thread or process.
:::

## Remote input

The [remote protocol](/Ion/tooling/remote-protocol/) registers `ScriptedInput` when it is enabled, and its `input.send`
method (a mutation, so the game must run with `--remote-allow-mutations`) queues events on it:

```bash
dotnet run -- --remote-allow-mutations
ion remote input.send '{"events":[{"type":"key","key":"Space","action":"tap"}]}'
```

Each event takes an optional `delay` in frames:

```json
{"type":"key","key":"Space","action":"tap|press|release|hold","frames":30,"modifiers":["Control"]}
{"type":"pointer","x":100,"y":200,"button":"Left","action":"move|click|press|release"}
{"type":"wheel","delta":1}
{"type":"text","text":"hello"}
{"type":"gamepad","index":0,"button":"A","action":"tap|press|release"}
{"type":"gamepad","index":0,"axis":"LeftX","value":0.5}
{"type":"gamepad","index":0,"action":"connect|disconnect"}
```

The result is `{queued, frame}`. Key, button and axis names are the enum names (`Key`, `MouseButton`, `GamepadButton`,
`GamepadAxis`). There is no touch event type. The [MCP server](/Ion/tooling/mcp-server/) exposes the same method as
the `ion_input` tool, so a coding agent can play the game. Because remote input goes through the recorder like device
input, an agent's session can be recorded with `AddInputRecording` and replayed as a test.

For menus, the UI's own remote methods (`ui.click`, `ui.set_value`, ...) are more robust than pixel coordinates; see
[UI remote](/Ion/interaction/ui/ui-remote/).

## See also

- [Input overview](/Ion/interaction/input/overview/)
- [Testing](/Ion/tooling/testing/) and [Snapshots and goldens](/Ion/tooling/snapshots-and-goldens/)
- [Remote protocol](/Ion/tooling/remote-protocol/) and [Agentic development](/Ion/tooling/agentic-development/)
- Source: [InputRecording.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Core.Abstractions/Input/InputRecording.cs),
  [ScriptedInput.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Core.Abstractions/Input/ScriptedInput.cs)
