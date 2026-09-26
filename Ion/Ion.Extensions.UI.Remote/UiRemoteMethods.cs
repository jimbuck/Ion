using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Ion.Extensions.Remote;

using static Ion.Extensions.Remote.RemoteSchema;

namespace Ion.Extensions.UI;

/// <summary>Registration of the UI's remote methods.</summary>
public static class UiRemoteExtensions
{
	/// <summary>
	/// Adds <c>ui.tree</c> (read, watchable), <c>ui.click</c>, <c>ui.set_value</c>, <c>ui.focus</c>, <c>ui.type</c> and
	/// <c>ui.back</c> (mutate) to the remote protocol, over the application's <see cref="IUiTree"/> (<c>AddUi</c>). Does
	/// nothing when the remote module is compiled out (<see cref="RemoteFeature.IsSupported"/>); the methods exist only
	/// while the remote server runs (<c>--remote</c>). Safe to call more than once.
	/// </summary>
	public static IServiceCollection AddUiRemote(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		if (RemoteFeature.IsSupported)
		{
			services.TryAddEnumerable(ServiceDescriptor.Singleton<IRemoteMethodProvider, UiRemoteMethods>(static sp => new UiRemoteMethods(sp)));
		}

		return services;
	}
}

/// <summary>
/// The UI's remote methods. The tree is read and commands are queued on the game thread (in the remote step at the end of
/// a frame); <see cref="IUiTree"/> applies commands at the start of the next frame's Update, so their effect shows in the
/// tree published at the end of that frame (<c>ui.tree</c> after one <c>game.step</c>, or on the next watch update).
/// </summary>
public sealed class UiRemoteMethods(IServiceProvider services) : IRemoteMethodProvider
{
	private IUiTree Tree => services.GetService(typeof(IUiTree)) as IUiTree
		?? throw new RemoteException(RemoteErrorCodes.Unsupported, "The game has no UI (AddUi).");

	private static JsonObject PathParam => Object(("path", String("The node's path, as ui.tree lists it (for example 'options/Volume').")));

	/// <inheritdoc/>
	public void Register(RemoteMethodRegistry methods)
	{
		ArgumentNullException.ThrowIfNull(methods);
		methods
			.Read("ui.tree", "The UI tree the last frame built: every node's path, kind, text, rectangle, state and value, and the focused path.", TreeResult,
				Object(("prefix?", String("Only nodes whose path starts with this."))), watchable: true)
			.Mutate("ui.click", "Clicks a node by path (a button reports it clicked, a toggle flips, a list item is selected, a text input starts editing), as the pointer would, at the start of the next frame.", Click, PathParam)
			.Mutate("ui.set_value", "Sets a toggle (true/false), slider (a number), text input (text) or list (an item's text or index) by path.", SetValue,
				Object(("path", String("The node's path.")), ("value", Of("string", "The value: text, or a number or boolean (converted with the invariant culture)."))))
			.Mutate("ui.focus", "Moves the focus to a focusable node.", Focus, PathParam)
			.Mutate("ui.type", "Types text into a text input (it takes focus and inserts at the caret), as keyboard text input would.", Type,
				Object(("path", String("The text input's path.")), ("text", String("The text to type."))))
			.Mutate("ui.back", "The back action (gamepad B, Escape): a text input being edited stops editing, otherwise the frame reports BackPressed.", Back);
	}

	private JsonNode TreeResult(RemoteRequest request)
	{
		var tree = Tree;
		var prefix = request.GetOptionalString("prefix");
		var nodes = new JsonArray();
		foreach (var node in tree.Snapshot())
		{
			if (prefix is not null && !node.Path.StartsWith(prefix, StringComparison.Ordinal)) continue;
			nodes.Add((JsonNode)ToJson(node));
		}

		return new JsonObject
		{
			["frame"] = tree.Frame,
			["version"] = tree.Version,
			["focused"] = tree.FocusedPath,
			["count"] = nodes.Count,
			["nodes"] = nodes,
		};
	}

	/// <summary>A node as <c>ui.tree</c> returns it.</summary>
	public static JsonObject ToJson(in UiNodeInfo node) => new()
	{
		["path"] = node.Path,
		["kind"] = KindName(node.Kind),
		["text"] = node.Text,
		["value"] = node.Value,
		["rect"] = new JsonArray(node.Rect.X, node.Rect.Y, node.Rect.Width, node.Rect.Height),
		["enabled"] = node.Enabled,
		["focusable"] = node.Focusable,
		["focused"] = node.Focused,
		["visible"] = node.Visible,
		["depth"] = node.Depth,
	};

	/// <summary>The lower-case kind name (<c>button</c>, <c>list_item</c>, ...).</summary>
	public static string KindName(UiNodeKind kind) => kind switch
	{
		UiNodeKind.Panel => "panel",
		UiNodeKind.Row => "row",
		UiNodeKind.Column => "column",
		UiNodeKind.ScrollView => "scroll_view",
		UiNodeKind.Label => "label",
		UiNodeKind.Button => "button",
		UiNodeKind.Toggle => "toggle",
		UiNodeKind.Slider => "slider",
		UiNodeKind.TextInput => "text_input",
		UiNodeKind.List => "list",
		UiNodeKind.ListItem => "list_item",
		UiNodeKind.Spacer => "spacer",
		_ => kind.ToString().ToLowerInvariant(),
	};

	private JsonNode Click(RemoteRequest request) => Queued(request, Tree.Click(request.GetString("path")), "clicked");

	private JsonNode Focus(RemoteRequest request) => Queued(request, Tree.Focus(request.GetString("path")), "focused");

	private JsonNode Type(RemoteRequest request) => Queued(request, Tree.Type(request.GetString("path"), request.GetString("text")), "typed into");

	private JsonNode SetValue(RemoteRequest request)
	{
		var value = request.Get("value") switch
		{
			null => throw RemoteException.InvalidParams("Missing required parameter 'value'."),
			JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
			JsonValue v when v.GetValueKind() == JsonValueKind.True => "true",
			JsonValue v when v.GetValueKind() == JsonValueKind.False => "false",
			JsonValue v when v.GetValueKind() == JsonValueKind.Number => v.GetValue<double>().ToString("R", CultureInfo.InvariantCulture),
			_ => throw RemoteException.InvalidParams("Parameter 'value' must be a string, a number or a boolean."),
		};
		return Queued(request, Tree.SetValue(request.GetString("path"), value), "set");
	}

	private JsonNode Back(RemoteRequest request)
	{
		Tree.Back();
		return new JsonObject { ["queued"] = true, ["frame"] = Tree.Frame };
	}

	private JsonNode Queued(RemoteRequest request, bool queued, string verb)
	{
		var path = request.GetString("path");
		if (!queued)
		{
			var tree = Tree;
			var reason = tree.TryFind(path, out var node)
				? node.Enabled ? $"a {KindName(node.Kind)} cannot be {verb}" : "the node is disabled"
				: "no node has this path in the current tree (see ui.tree)";
			throw new RemoteException(RemoteErrorCodes.NotFound, $"'{path}' cannot be {verb}: {reason}.", new JsonObject { ["path"] = path });
		}

		return new JsonObject { ["queued"] = true, ["path"] = path, ["frame"] = Tree.Frame };
	}
}
