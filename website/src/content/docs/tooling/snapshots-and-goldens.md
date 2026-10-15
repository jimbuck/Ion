---
title: Snapshots and golden images
description: Lock in game state and rendered frames with JSON snapshots and golden PNGs, compare them with tolerances, and update them on purpose.
sidebar:
  order: 5
---

A snapshot test runs the game to a known point and compares something with a file committed next to the test: the ECS
world or any JSON as a **snapshot**, a rendered frame as a **golden image**. Because headless runs are deterministic
(fixed clock, fixed seed), the same run produces the same file, and any difference is a change in behavior you either
fix or accept.

```csharp
[Fact]
public void WorldAfter120FramesMatchesTheSnapshot()
{
	using var run = IonTestHost.RunEntryPoint<Program>(120, host => host.WithConfiguration("Ion:Seed", "1"));
	JsonSnapshot.AssertMatches(run.WorldJson()!, RenderingEnvironment.GoldenPath("world-120.json"));
}
```

Both kinds share one update policy:

| Situation | What happens |
|---|---|
| The file does not exist | The actual value is written there and the test **fails**, so a missing file never passes silently on CI. Review it, commit it, run again. |
| The values match | The test passes. |
| The values differ | The test fails; the actual value is written next to the file (`*.actual.json`, `*.actual.png`, and for images `*.diff.png`). |
| `ION_UPDATE_GOLDEN=1` (or `true`) | Every file is overwritten with the actual value and the test passes. |

## Where the files live

`RenderingEnvironment.GoldenPath(name)` returns `Golden/<name>` in the directory of the **calling source file** (it uses
`[CallerFilePath]`), so updates land in your repository rather than in `bin/`. When the sources are not there (a test run
from a published folder), it falls back to `Golden/` under the output directory. The templates copy `Golden/**` to the
output as well:

```xml title="MyGame.Tests.csproj"
<ItemGroup>
  <None Include="Golden\**\*" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Keep the generated `*.actual.*` and `*.diff.png` files out of source control; the templates' `.gitignore` does.

## JSON snapshots

`JsonSnapshot.AssertMatches(actualJson, snapshotPath, options?)` normalizes the JSON, then compares it with the file. It
returns the normalized JSON. On a mismatch it throws `JsonSnapshotException` naming the first differing line:

```text
JSON does not match snapshot '.../Golden/state-300.json': line 4: expected `"Score": 3,`, actual `"Score": 4,`.
Actual JSON written to '.../Golden/state-300.actual.json'.
```

### Normalization

Normalization makes snapshots stable across machines, CPUs and JIT tiers:

- Numbers are rounded to `Decimals` places (default 3) and written without a trailing `.0`, so floating point noise does
  not fail a test.
- `-0` becomes `0`.
- Properties named in `Ignore` are dropped at any depth (timestamps, ids that vary).
- Object properties are sorted by name when `SortProperties` is set.
- The text is indented with `\n` line endings and ends with a newline.

`JsonSnapshotOptions` is a record; start from `JsonSnapshotOptions.Default`:

| Property | Default | Meaning |
|---|---|---|
| `Decimals` | `3` (`JsonSnapshot.DefaultDecimals`) | Decimals numbers are rounded to. |
| `Ignore` | empty | Property names dropped at any depth. |
| `SortProperties` | `false` | Sort object properties by name. |

```csharp
var options = JsonSnapshotOptions.Default with
{
	Decimals = 2,
	Ignore = new HashSet<string> { "startedAt", "pid" },
	SortProperties = true,
};
JsonSnapshot.AssertMatches(json, RenderingEnvironment.GoldenPath("info.json"), options);
```

`JsonSnapshot.Normalize(json, options?)` is public too, for comparing two values in memory.

### Snapshotting game state

Anything you can serialize works. The 2D template snapshots its play state with source-generated JSON:

```csharp
[Fact]
public void StateAfter300FramesMatchesTheSnapshot()
{
	using var run = IonTestHost.RunEntryPoint<Program>(300, host => host.WithConfiguration("Ion:Seed", "1"));
	var json = JsonSerializer.Serialize(run.Get<PlayState>(), GameJson.Default.PlayState);
	JsonSnapshot.AssertMatches(json, RenderingEnvironment.GoldenPath("state-300.json"));
}
```

### World snapshots

For ECS games, `run.WorldJson(decimals)` returns the most recent live world (the active scene's, or the root one) in the
world serializer's format: every entity with its registered components. Only components registered with
`AddEcsSerialization` appear, which is also what makes them visible to the [remote protocol](/Ion/tooling/remote-protocol/).
See [Components and serialization](/Ion/ecs/components-and-serialization/).

`WorldSnapshot` does the same for any `World`:

```csharp
var world = host.Get<EcsWorlds>().Root;
WorldSnapshot.AssertMatches(world, RenderingEnvironment.GoldenPath("world.json"), host.Get<ComponentSerializerRegistry>());

string json = WorldSnapshot.ToJson(world);   // the built-in components only when no registry is given
```

:::tip[Snapshot something small]
A snapshot is only as useful as its diff is readable. Snapshot after a short run (120 to 300 frames), keep entity counts
small, and prefer a focused state object over the whole world when that says what the test is about.
:::

## Golden images

Golden images compare rendered frames. They need headless rendering: `host.WithRendering()` in a test, or
`ion run --headless --screenshot` from the command line, and a Vulkan driver or EGL with OpenGL ES 3 (Mesa lavapipe or
llvmpipe on Linux CI).

```csharp
[Fact]
public void TitleScreenMatchesTheGolden()
{
	if (!RenderingEnvironment.HasVulkan && !RenderingEnvironment.HasHeadlessGles) return; // or use a skipping Fact attribute

	using var run = IonTestHost.RunEntryPoint<Program>(60, host => host
		.WithRendering(640, 360)
		.WithConfiguration("Ion:Seed", "1"));

	GoldenImage.AssertMatches(run.Image!, RenderingEnvironment.GoldenPath("title-60.png"), tolerance: 2);
}
```

### The comparison rule

A pixel mismatches when any RGBA channel differs by more than `tolerance` (0 to 255). GPU drivers differ in rounding
and rasterization, so a small tolerance (the default is 2) absorbs that without hiding real changes.

| API | Meaning |
|---|---|
| `GoldenImage.AssertMatches(actual, goldenPath, tolerance = 2, maxMismatchRatio = 0)` | Passes when at most `maxMismatchRatio` of the pixels mismatch. Returns the `GoldenComparison`; throws `GoldenImageException` otherwise. |
| `GoldenImage.Compare(actual, expected, tolerance = 2)` | Compares two `Screenshot`s without files. |
| `GoldenImage.Load(path)` | Reads a PNG as a `Screenshot`. |
| `GoldenImage.Save(screenshot, path)` | Writes a PNG, creating the directory. |
| `GoldenImage.UpdateVariable` | `"ION_UPDATE_GOLDEN"`. |

`GoldenComparison` has `SameSize`, `MismatchedPixels`, `MaxChannelDifference`, `ActualSize`, `ExpectedSize` and
`FirstMismatch`. Images of different sizes never match. On a mismatch, `<golden>.actual.png` and `<golden>.diff.png` are
written next to the golden file; the diff shows mismatching pixels in red over a dimmed copy of the golden.

```text
Image does not match golden '.../Golden/title-60.png': 412 mismatched pixels, max channel difference 96, first at (310, 172).
Actual image written to '.../Golden/title-60.actual.png'.
```

### From the command line

`ion diff` applies the same rule to two files, which is what an agent without a test project uses:

```bash
ion run --headless --frames 600 --seed 1 --screenshot out/frame600.png
mkdir -p Golden && cp out/frame600.png Golden/frame600.png       # accept the first run after reviewing it
ion run --headless --frames 600 --seed 1 --screenshot out/again.png
ion diff out/again.png Golden/frame600.png                      # exit 0 on a match, 1 on a mismatch
```

`--tolerance` and `--max-ratio` match the test API's parameters; `--out` sets the diff image path. The MCP server's
`ion_diff` tool is the same comparison. See [ion diff](/Ion/tooling/ion-cli/#ion-diff).

## Updating on purpose

When a change is intended, regenerate the files and review the diff before committing:

```bash
ION_UPDATE_GOLDEN=1 dotnet test
git diff -- '*Golden*'
```

```powershell
$env:ION_UPDATE_GOLDEN = "1"; dotnet test; Remove-Item Env:ION_UPDATE_GOLDEN
```

:::caution
Never set `ION_UPDATE_GOLDEN` on CI: every snapshot test would pass by rewriting its own expectation. Update locally,
look at the JSON diff or open the new PNGs, and commit only what you meant to change.
:::

To update one test, filter it: `ION_UPDATE_GOLDEN=1 dotnet test --filter WorldAfter120FramesMatchesTheSnapshot`.

## Making runs reproducible

Snapshots only work when the run is deterministic:

- Headless runs from the test host always use the fixed-step clock; `ion run --headless --frames N` does too.
- Pass a seed (`WithConfiguration("Ion:Seed", "1")`, `--seed 1`) and read it with `IonRun.Seed(config)`. Do not use
  `Random.Shared` or wall-clock time in gameplay.
- Golden images depend on the backend and driver. Pin `Ion:Graphics:PreferredBackend` in the test if you run on more
  than one, or keep one golden per backend.

See [Time and determinism](/Ion/concepts/time-and-determinism/).

## See also

- [Testing](/Ion/tooling/testing/): `IonTestHost`, `RunEntryPoint` and headless rendering.
- [The ion command line](/Ion/tooling/ion-cli/): `ion run --screenshot` and `ion diff`.
- [Agentic development](/Ion/tooling/agentic-development/): the "lock it in" step of the agent loop.
- [Components and serialization](/Ion/ecs/components-and-serialization/): what world snapshots contain.
