---
title: UI remote
description: Inspect and drive Ion UI by path through IUiTree in tests, and through the ui.* remote protocol methods from tools, the ion CLI and coding agents.
sidebar:
  order: 5
---

The UI publishes what it built each frame as a tree of nodes with stable paths, and accepts commands by path: click,
set a value, focus, type, back. Tests use it through `IUiTree`; tools and agents use the same surface over the remote
protocol (`ui.tree`, `ui.click`, ...). No pixels, screenshots or coordinates are needed to drive a menu.

```csharp
var tree = host.Get<IUiTree>();

Assert.True(tree.Click("main/Options"));          // queued for the next frame
host.Step(2);                                      // applied, then the new screen is built
Assert.True(tree.SetValue("options/Volume", "0.35"));
host.Step();
Assert.True(tree.TryFind("options/Volume", out var volume));
Assert.Equal("0.35", volume.Value);
```

## IUiTree

`IUiTree` lives in `Ion.Extensions.UI.Abstractions`, which has no dependencies, so tools can reference it without the
UI module. `AddUi` registers the application's instance (the same object as `Ui.Tree`).

| Member | Meaning |
|---|---|
| `Frame` | The number of UI frames built; the tree describes the last one. |
| `Version` | Changes only when the set or order of paths changes. |
| `Count`, `this[index]` | The nodes in pre-order: parents before children, siblings in call order. |
| `FocusedPath` | The focused node's path, or null. |
| `TryFind(path, out node)` | Finds a node by exact, case-sensitive path. |
| `Snapshot()` | A copy of every node. Safe to call from any thread. |
| `Click(path)`, `SetValue(path, value)`, `Focus(path)`, `Type(path, text)` | Queue a command; false when it cannot apply. |
| `Back()` | Queue a back action. |

Reading members (`Count`, the indexer, `TryFind`, `FocusedPath`) are meant for the game thread; use `Snapshot()` from
other threads. Commands are thread-safe.

### Nodes

Each node is a `UiNodeInfo`:

| Field | Meaning |
|---|---|
| `Path` | The node's path (below). |
| `Kind` | `UiNodeKind`: `Panel`, `Row`, `Column`, `ScrollView`, `Label`, `Button`, `Toggle`, `Slider`, `TextInput`, `List`, `ListItem`, `Spacer`. |
| `Text` | The caption or text shown, or null. |
| `Rect` | The layout rectangle in window pixels (`UiRect`: `X`, `Y`, `Width`, `Height`, plus `Right`, `Bottom`, `CenterX`, `CenterY`, `Contains`). Not clipped. |
| `Enabled` | False inside a `Disabled` scope. |
| `Focusable`, `Focused` | Focus state. |
| `Visible` | False when scrolled entirely out of an enclosing scroll view. |
| `Value` | The value as text (below), or null. |
| `Depth`, `Parent` | Nesting depth (0 for top-level nodes) and the parent's index (-1 at the top). |

| Kind | `Value` |
|---|---|
| `Toggle` | `true` or `false` |
| `Slider` | The number, invariant culture, up to three decimals (`0.35`) |
| `TextInput` | The text |
| `List` | The selected item's text |
| `ListItem` | `true` when selected |
| `ScrollView` | The scroll offset in pixels |

### Paths

A node's path is its ancestors' segments and its own, joined with `/`. The segment is:

1. the widget's explicit `key`, when given;
2. otherwise its caption, for buttons, toggles, sliders, text inputs, lists and list items (a `/` in it becomes `_`);
3. otherwise its kind in lower case: `panel`, `row`, `column`, `scroll`, `label`, `list`, `item`, `spacer`.

A segment repeated among siblings gets `#2`, `#3`, ... in call order. Examples from the Menu sample:

```
main
main/title              Label("Ion Menu", "title", ...)
main/greeting           Label(..., "greeting", ...)
main/Play               Button("Play")
options/Volume          Slider("Volume", ...)
options/Difficulty/Hard ListItem of List("Difficulty", ...)
```

Paths are stable as long as the same calls are made: a label's changing text does not change its path (labels are
named by key or `label`), and `Version` changes only when the sequence of paths does. Give keys to labels, containers
and anything whose caption changes, so tests and agents can address them.

:::tip[Design for addressable paths]
Key the top-level panel of each screen (`ui.Panel("options")`), give changing labels a key (`"score"`, `"status"`),
and give buttons stable captions or keys. Then every screen reads like an API: `options/Back`, `hud/score`.
:::

### Commands

Each command is checked against the published tree when you call it. It returns false, and queues nothing, when the
path does not exist, the node is disabled, or the command does not apply to its kind. Accepted commands are applied in
order at the start of the next frame's Update, before input and before any widget call, exactly as the equivalent
input would be:

| Command | Applies to | Effect |
|---|---|---|
| `Click(path)` | `Button`, `Toggle`, `ListItem`, `TextInput` | A button's call returns true, a toggle flips, an item is selected, a text input starts editing. Also moves the focus. |
| `SetValue(path, value)` | `Toggle` (`true`/`false`), `Slider` (a number, clamped and snapped), `TextInput` (text, cut to its maximum length), `List` (an item's text or its index) | The value is written through the widget's `ref` parameter. |
| `Focus(path)` | Any focusable node | Moves the focus (and scrolls it into view). |
| `Type(path, text)` | `TextInput` | Focuses it, starts editing and inserts the text at the caret. |
| `Back()` | | The frame reports `Ui.BackPressed`, or a text field being edited stops editing. |

Timing: a command queued between frames N and N+1 is applied at the start of N+1's Update; the tree published at the
end of N+1 shows its effect. A screen change it causes (your code switches screens in response) is built in N+2. That
is why tests step twice after a click that changes screens, or use `RunUntil`:

```csharp
Assert.True(tree.Click("main/Options"));
Assert.True(host.RunUntil(() => tree.TryFind("options/Back", out _), maxFrames: 5));
```

Several clicks on one button in one frame count once.

## Testing through the tree

The [Menu](/Ion/examples/menu/) sample's tests drive every screen through `IUiTree` only, which is also exactly what an
agent can do remotely:

```csharp title="MenuTreeTests.cs"
using Ion.Extensions.UI;
using Ion.Testing;
using Xunit;

public class MenuTreeTests
{
	[Fact]
	public void AnAgentChangesEveryOption()
	{
		using var host = new IonTestHost().UseEntryPoint<Program>();
		host.Step();
		var tree = host.Get<IUiTree>();
		var settings = host.Get<MenuSettings>();

		Assert.Equal(["main", "main/title", "main/greeting", "main/Play", "main/Options", "main/Quit"],
			tree.Snapshot().Select(n => n.Path));
		Assert.Equal("main/Play", tree.FocusedPath);

		Assert.True(tree.Click("main/Options"));
		Assert.True(host.RunUntil(() => tree.TryFind("options/Back", out _), maxFrames: 5));

		Assert.True(tree.Click("options/Fullscreen"));
		Assert.True(tree.SetValue("options/Volume", "0.35"));
		Assert.True(tree.Click("options/Difficulty/Hard"));
		Assert.True(tree.SetValue("options/Name", "Ada"));
		host.Step();
		Assert.True(tree.Type("options/Name", " L."));
		host.Step();

		Assert.True(settings.Fullscreen);
		Assert.Equal(0.35f, settings.Volume, 5);
		Assert.Equal(2, settings.Difficulty);
		Assert.Equal("Ada L.", settings.PlayerName);
	}

	[Fact]
	public void TheTreeIsStableWhileNothingHappens()
	{
		using var host = new IonTestHost().UseEntryPoint<Program>();
		host.Step();
		var tree = host.Get<IUiTree>();
		var version = tree.Version;
		var before = tree.Snapshot();
		host.Step(30);
		Assert.Equal(version, tree.Version);
		Assert.Equal(before, tree.Snapshot());
	}
}
```

Rectangles let you mix in real pointer input when you want to test hit testing too:

```csharp
Assert.True(tree.TryFind("options/Back", out var back));
host.Input.SetMousePosition(new Vector2(back.Rect.CenterX, back.Rect.CenterY));
host.Step();
host.Input.Click();
host.Step(2);
```

Because layout works without a font (see [Layout and styling](/Ion/interaction/ui/layout-and-styling/#font)), these
tests run headless with no GPU. For a pixel check of a screen, add a golden image with
[headless rendering](/Ion/tooling/snapshots-and-goldens/).

## The remote methods

`AddUiRemote()` adds the UI's methods to the [remote protocol](/Ion/tooling/remote-protocol/):

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddUi().AddUiRemote().AddSystem<MenuSystem>();

using var game = builder.Build();
game.UseUi().UseSystem<MenuSystem>();
game.Run();
```

`builder.AddUiRemote()` also calls `AddUi` (and so the engine core, which carries the remote protocol). The methods
exist only while the remote server runs (`--remote`, or `--remote-allow-mutations` for the commands), and nothing is
registered when the remote module is compiled out of a Release build. The service collection form is
`services.AddUiRemote()` (package `Ion.Extensions.UI.Remote`). Calling it twice is safe.

| Method | Access | Params | Result |
|---|---|---|---|
| `ui.tree` (+watch) | read | `prefix?` | `{frame, version, focused, count, nodes[{path, kind, text, value, rect[x, y, w, h], enabled, focusable, focused, visible, depth}]}` |
| `ui.click` | mutate | `path` | `{queued, path, frame}` |
| `ui.set_value` | mutate | `path`, `value` (string, number or boolean) | `{queued, path, frame}` |
| `ui.focus` | mutate | `path` | `{queued, path, frame}` |
| `ui.type` | mutate | `path`, `text` | `{queued, path, frame}` |
| `ui.back` | mutate | | `{queued, frame}` |

Kinds are lower case with underscores in JSON: `panel`, `row`, `column`, `scroll_view`, `label`, `button`, `toggle`,
`slider`, `text_input`, `list`, `list_item`, `spacer`. `ui.tree` is watchable: subscribe to it over WebSocket to get a
new tree as the UI changes. Numbers and booleans passed to `ui.set_value` are converted with the invariant culture.

A command that cannot apply fails with error -32004 and a message that says why: no node has that path in the current
tree, the node is disabled, or its kind cannot take that command (for example "a label cannot be clicked"). Without
`AddUi`, the methods fail with -32005 (unsupported).

The methods run on the game thread in the remote step at the end of a frame and queue their commands, so as with
`IUiTree`, the effect shows in the tree published at the end of the next frame: call `ui.tree` after one `game.step`,
or wait for the next watch update.

### From the command line

```bash
dotnet run --project Ion.Examples/Ion.Examples.Menu -- --remote-allow-mutations

ion remote ui.tree
ion remote ui.tree '{"prefix":"options/"}'
ion remote ui.click '{"path":"main/Options"}'
ion remote ui.set_value '{"path":"options/Volume","value":0.35}'
ion remote ui.type '{"path":"options/Name","text":" L."}'
ion remote ui.back
```

`ion remote` finds the endpoint and token through the game's token file (`--project` or `--token-file`). See
[ion CLI](/Ion/tooling/ion-cli/).

### From an agent

The [MCP server](/Ion/tooling/mcp-server/) (`ion mcp`) has `ion_ui_tree` (with an optional `prefix`) and
`ion_ui_click` (a `path`) tools, and `ion_call` reaches the other `ui.*` methods. An agent reads the tree, finds the
widget by path or text, acts, steps a frame and reads the tree again, with no screenshots. The Menu sample's
`MenuRemoteTests` is exactly that, as a plain JSON-RPC client over HTTP with only the token file:

```csharp
Call("ui.click", new JsonObject { ["path"] = "main/Options" });
WaitFor("options/Back");
Call("ui.set_value", new JsonObject { ["path"] = "options/Volume", ["value"] = 0.35 });
Call("ui.type", new JsonObject { ["path"] = "options/Name", ["text"] = " L." });
Call("ui.focus", new JsonObject { ["path"] = "options/Back" });
```

See [Agentic development](/Ion/tooling/agentic-development/).

## Tree commands versus input

| | `IUiTree` / `ui.*` | Scripted or remote input (`input.send`) |
|---|---|---|
| Addresses widgets by | Path | Coordinates and keys |
| Survives layout changes | Yes | No |
| Tests hit testing, focus movement, key repeat | No | Yes |
| Works on disabled widgets | No (refused) | No (ignored) |

Use the tree for flows ("change these options and play") and input for the input handling itself. See
[Recording and playback](/Ion/interaction/input/recording-and-playback/).

## See also

- [UI overview](/Ion/interaction/ui/overview/)
- [Remote protocol](/Ion/tooling/remote-protocol/) and [MCP server](/Ion/tooling/mcp-server/)
- [Testing](/Ion/tooling/testing/)
- [Menu example](/Ion/examples/menu/)
- Source: [IUiTree.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.UI.Abstractions/IUiTree.cs),
  [UiRemoteMethods.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.UI.Remote/UiRemoteMethods.cs)
