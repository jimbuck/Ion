---
title: Mobile (Android and iOS)
description: Build the Android and iOS heads of an Ion game with IonMobileHeads, share the game code with the desktop project, and read touch input.
sidebar:
  order: 3
---

Ion runs phones and tablets through **head projects**: a small Android or iOS app that compiles the game's shared code
and calls the same `AddX`/`UseX` setup as the desktop `Program.cs`. The repository ships two, for the Breakout ECS
sample: `Ion.Examples.Breakout.ECS.Android` and `Ion.Examples.Breakout.ECS.iOS`.

:::caution[Status: compiles, not yet run on a device]
As of September 2026 the Android head compiles for `net10.0-android`, but packaging needs the Android SDK, which was not
reachable from the build container, and the iOS workload does not install on Linux. Neither head has run on a phone or
tablet. The shared entry point (`BreakoutMobile.Run`) is tested on the desktop, headless. Expect to do the first device
run yourself.
:::

## How a head is put together

```
Ion.Examples.Breakout.ECS/            the desktop game (Program.cs) and the shared code
  BreakoutGame.cs                     AddBreakout() / UseBreakout(): the whole game as a module
  BreakoutSystems.cs                  components, events and systems
  BreakoutMobile.cs                   the mobile entry point (Arguments, Run)
Ion.Examples.Breakout.ECS.Android/    net10.0-android app: MainActivity (SilkActivity)
Ion.Examples.Breakout.ECS.iOS/        net10.0-ios app: Program.cs (SilkMobile.RunApp)
```

The key design choice: **the game is a module**, not a `Program.cs`. `BreakoutGame.AddBreakout()` registers everything
and `UseBreakout()` adds the systems, so three entry points (desktop, Android, iOS) make the same two calls. Each head
links the shared sources instead of referencing the desktop executable:

```xml title="Ion.Examples.Breakout.ECS.Android.csproj (excerpt)"
<ItemGroup>
  <Compile Include="..\Ion.Examples.Breakout.ECS\BreakoutGame.cs;..\Ion.Examples.Breakout.ECS\BreakoutSystems.cs;..\Ion.Examples.Breakout.ECS\BreakoutMobile.cs" Link="Game\%(Filename)%(Extension)" />
  <Compile Include="..\Ion.Examples.Breakout.ECS\Common\**\*.cs" Link="Game\Common\%(RecursiveDir)%(Filename)%(Extension)" />
  <!-- The Android entry point compiles only into the real head. -->
  <Compile Remove="Platform\**" Condition="'$(IonBuildMobileHead)' != 'true'" />
</ItemGroup>
```

The shared entry point builds the configuration for a phone and runs the game:

```csharp title="BreakoutMobile.cs"
public static class BreakoutMobile
{
	public static string[] Arguments(string contentRoot, IReadOnlyList<string> extra) =>
	[
		$"--Ion:Storage:GamePath={contentRoot}",
		"--Ion:Title=Ion Breakout",
		"--Ion:Window:Platform=Sdl",
		"--Ion:Window:Fullscreen=true",
		"--Ion:Window:ShowCursor=false",
		.. extra,
	];

	public static void Run(string contentRoot, IReadOnlyList<string> extra)
	{
		var builder = IonApplication.CreateBuilder(Arguments(contentRoot, extra));
		builder.AddBreakout();

		using var game = builder.Build();
		game.UseBreakout();

		game.Run();
	}
}
```

It does not set a graphics backend: `GraphicsBackend.Auto` tries Vulkan first, then OpenGL ES (Vulkan runs over
MoltenVK on iOS).

## The platform entry points

### Android

The activity derives from Silk.NET's `SilkActivity` (SDL's Java activity, `libSDL2.so` and `libmain.so` for every ABI),
which calls `OnRun` on SDL's main thread. Ion loads content from files, and APK assets are not files, so the activity
unpacks them into the app's files folder first:

```csharp title="Platform/MainActivity.cs"
[Activity(Label = "Ion Breakout", MainLauncher = true, Theme = "@android:style/Theme.NoTitleBar.Fullscreen",
	ScreenOrientation = ScreenOrientation.SensorLandscape, LaunchMode = LaunchMode.SingleInstance,
	ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard | ConfigChanges.UiMode)]
public sealed class MainActivity : SilkActivity
{
	protected override void OnRun()
	{
		var root = Path.Combine(FilesDir!.AbsolutePath, "game");
		Unpack(Assets!, Path.Combine(root, "Assets"));
		BreakoutMobile.Run(root, []);
	}

	// Unpack copies the APK's top-level asset files into the destination folder.
}
```

The head's project adds the game's `Assets` folder as `AndroidAsset` items and references `Silk.NET.Windowing.Sdl`. It
targets `net10.0-android` with `SupportedOSPlatformVersion` 26, runtime identifiers `android-arm64` and `android-x64`,
produces an `.apk`, and uses NativeAOT in Release (Mono in Debug).

### iOS

SDL owns the UIKit main loop on iOS. `SilkMobile.RunApp` starts it and calls the game on SDL's main thread. The assets
are bundle resources, so the content root is the app bundle:

```csharp title="Platform/Program.cs"
using Silk.NET.Windowing.Sdl.iOS;

using Ion.Examples.Breakout.ECS;

SilkMobile.RunApp(args, static a => BreakoutMobile.Run(AppContext.BaseDirectory, a));
```

The head targets `net10.0-ios` (`SupportedOSPlatformVersion` 15.0, `ios-arm64` by default), bundles the assets with
`BundleResource` items, references `Silk.NET.Windowing.Sdl` and `Silk.NET.MoltenVK.Native` (MoltenVK for the Vulkan
backend), and uses NativeAOT in Release.

## Building the heads

Both heads build only when you opt in with `-p:IonMobileHeads=true` on a machine with the workload:

```bash
dotnet workload install android      # plus a JDK and the Android SDK
dotnet publish Ion.Examples/Ion.Examples.Breakout.ECS.Android -c Release -p:IonMobileHeads=true -p:RuntimeIdentifier=android-arm64

dotnet workload install ios          # macOS with Xcode only
dotnet publish Ion.Examples/Ion.Examples.Breakout.ECS.iOS -c Release -p:IonMobileHeads=true
```

A device build for iOS needs a signing identity and a provisioning profile. CI builds the iOS head for the simulator
instead, which needs no signing:

```bash
dotnet build Ion.Examples/Ion.Examples.Breakout.ECS.iOS -c Release -p:IonMobileHeads=true -p:RuntimeIdentifier=iossimulator-arm64
```

### How IonMobileHeads works

`build/Ion.Mobile.props` is imported by each head before its target framework is chosen. It detects the workloads and
decides what the head builds as:

| Property | Meaning |
|---|---|
| `IonMobileHeadKind` | Set by the head before the import: `android` or `ios`. |
| `IonMobileHeads` | Opt in to the real heads. Default `false`. |
| `IonAndroidWorkload`, `IonIosWorkload` | `true` when the workload's SDK pack is installed (machine-wide under the .NET root or user-local under `~/.dotnet`). |
| `IonBuildMobileHead` | `true` when `IonMobileHeads` is on and the head's workload is installed; picks the mobile target framework. |

Without `IonMobileHeads` (the default, and how `Ion.sln` builds) each head builds as a `net10.0` **library** of the
shared game code, with its platform entry point left out. That keeps the shared code compiling in every desktop build
and test run, on machines without any mobile workload.

:::danger[IONMOB001]
`-p:IonMobileHeads=true` on a machine without the head's workload stops the build with `IONMOB001`: "IonMobileHeads=true,
but the android workload is not installed (dotnet workload install android)." Install the workload, or build without
the property.
:::

CI has `mobile-android` and `mobile-ios` jobs that run only when the repository variable `ION_MOBILE_CI` is `true`.

## Windowing on mobile

On Android and iOS the windowing module uses SDL (`Ion:Window:Platform` = `Auto` picks it there). SDL is view-only on
these platforms, so `SilkWindow` creates a full-screen view (`SilkWindow.IsViewOnly` is true) and the window-only
setters (title, size, position, state, border) do nothing. Size your game from `IWindow.Width`, `IWindow.Height` and
`WindowResizeEvent` rather than setting a size.

:::caution[Fixed-size layouts]
Breakout's play field is a fixed 2030 x 984 window-space layout. On a phone, as on the R36S's 640 x 480 panel, it needs
a scaled view (a virtual resolution in the 2D renderer), which is not done yet. Plan your own layout around the window
size, or scale with a camera transform (`SpriteBatchOptions.Transform`).
:::

## Touch input

The SDL platform maps finger events into `IInputState.Touches`, a `ReadOnlySpan<TouchPoint>` of up to
`InputTracker.MaxTouches` (10) fingers in the order they began. SDL also synthesizes mouse events from touches by
default, so a mouse-driven game keeps working, and the UI module receives touch as the mouse.

| `TouchPoint` member | Meaning |
|---|---|
| `Id` | Stable from the frame the touch began to the frame it ended; small (0 to 9) and reused. |
| `Position`, `Delta`, `StartPosition` | Window coordinates (the same space as `MousePosition`). |
| `Phase` | `Began`, `Moved`, `Stationary`, `Ended` or `Canceled`. |
| `Pressed` | The touch began this frame. |
| `Released`, `IsDown` | The touch ended or was cancelled this frame; the finger is still down. |

A tap that goes down and up within one frame is reported once, with `Pressed` and `Released` both true, so it is never
lost. `input.TryGetTouch(id, out var touch)` finds one touch by id.

Breakout ECS moves its paddle with the first finger and launches a ball when it lifts:

```csharp title="BreakoutSystems.cs"
[Update]
public void Touch(GameTime dt)
{
	var touches = input.Touches;
	if (touches.IsEmpty) return;

	var touch = touches[0];
	ref var paddleTransform = ref _paddle.Get<Transform2D>();
	paddleTransform.Position = new Vector2(touch.Position.X, paddleTransform.Position.Y);

	if (touch.Released)
	{
		events.Emit(new LaunchBallCommand());
	}
}
```

:::note[Read touches in Update]
Touches have a per-frame view only. From `FixedUpdate` you see the same list as from `Update`, so a touch that begins
and ends between two fixed steps would be missed by a fixed step. Read them in `Update`, as above.
:::

### Testing touch without a phone

The headless input (`NullInputState`, `host.Input` in `IonTestHost`) scripts touches with `TouchDown`, `TouchMove` and
`TouchUp`, so touch code is testable on the desktop:

```csharp title="BreakoutHeadlessTests.cs"
using var host = new IonTestHost(TimeSpan.FromSeconds(1.0 / 120)).UseEntryPoint<Program>();
var launches = host.Collect<LaunchBallCommand>();

host.Step();
host.Input.TouchDown(0, new Vector2(300, 500));
host.Step();
host.Input.TouchUp(0, new Vector2(700, 480));
host.Step();

Assert.Single(launches);
```

The mobile entry point itself is tested headless too:

```csharp title="BreakoutMobileTests.cs"
BreakoutMobile.Run(AppContext.BaseDirectory, ["--Ion:Headless=true", "--Ion:Run:Frames=30", "--Logging:LogLevel:Default=Warning"]);
```

## Making your own game mobile-ready

1. Move the setup out of `Program.cs` into a pair of extension methods, as `AddBreakout`/`UseBreakout` do, so every
   entry point calls the same code.
2. Put a mobile `Run(contentRoot, extra)` next to it that sets `Ion:Storage:GamePath`, the SDL platform and fullscreen.
3. Copy the two head projects, change `ApplicationId`, `ApplicationTitle` and the linked source files, and keep the
   `Import` of `build/Ion.Mobile.props`.
4. Read touch input in `Update` and lay out from the window size.
5. Register the component types your game only creates with `EcsComponents.Register<T>()`, as NativeAOT needs them
   up front (see [NativeAOT](/Ion/platforms/native-aot/)).

## See also

- [Breakout ECS example](/Ion/examples/breakout-ecs/): the game the heads run.
- [Touch input](/Ion/interaction/input/touch/): the full touch API.
- [Platforms overview](/Ion/platforms/overview/) and [Publishing](/Ion/platforms/publishing/).
