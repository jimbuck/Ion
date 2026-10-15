using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Arch.Core;

using Ion.Extensions.Ecs;

namespace Ion.Testing;

/// <summary>
/// Snapshot testing for JSON state (a world, a remote query result, a summary): normalize, then compare with a committed
/// <c>.json</c> file, with the same update policy as <see cref="GoldenImage"/>.
/// </summary>
/// <remarks>
/// <para>
/// Normalization makes snapshots stable across machines: numbers are rounded to a number of decimals (floating point noise
/// from a different CPU or JIT does not fail a test) and written without a trailing <c>.0</c>, <c>-0</c> becomes <c>0</c>,
/// the properties named in <see cref="JsonSnapshotOptions.Ignore"/> are dropped (timestamps, ids that vary), object
/// properties are optionally sorted, and the text is indented with <c>\n</c> line endings.
/// </para>
/// <para>
/// Updating: when the snapshot file does not exist, <see cref="AssertMatches"/> writes the actual JSON there and fails (so
/// a missing snapshot never passes silently on CI); set <see cref="GoldenImage.UpdateVariable"/> (<c>ION_UPDATE_GOLDEN=1</c>)
/// to overwrite snapshots and pass. On a mismatch <c>&lt;snapshot&gt;.actual.json</c> is written next to it and the message
/// names the first differing line.
/// </para>
/// </remarks>
public static class JsonSnapshot
{
	/// <summary>The default number of decimals numbers are rounded to.</summary>
	public const int DefaultDecimals = 3;

	/// <summary>Normalizes <paramref name="json"/> (see remarks).</summary>
	public static string Normalize(string json, JsonSnapshotOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(json);
		options ??= JsonSnapshotOptions.Default;
		var node = JsonNode.Parse(json);
		var normalized = NormalizeNode(node, options);
		var text = normalized?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
		return text.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
	}

	/// <summary>
	/// Asserts that <paramref name="actualJson"/>, normalized, equals the snapshot at <paramref name="snapshotPath"/>.
	/// Returns the normalized JSON.
	/// </summary>
	/// <exception cref="JsonSnapshotException">The JSON differs, or the snapshot was missing (it has now been written).</exception>
	public static string AssertMatches(string actualJson, string snapshotPath, JsonSnapshotOptions? options = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(snapshotPath);
		var actual = Normalize(actualJson, options);
		var update = Environment.GetEnvironmentVariable(GoldenImage.UpdateVariable) is "1" or "true";
		if (update || !File.Exists(snapshotPath))
		{
			Write(snapshotPath, actual);
			if (update) return actual;
			throw new JsonSnapshotException($"Snapshot '{snapshotPath}' did not exist; the actual JSON was written there. Review it, commit it and run the test again.");
		}

		var expected = Normalize(File.ReadAllText(snapshotPath), options);
		if (expected == actual) return actual;

		var actualPath = Path.ChangeExtension(snapshotPath, ".actual.json");
		Write(actualPath, actual);
		throw new JsonSnapshotException($"JSON does not match snapshot '{snapshotPath}': {FirstDifference(expected, actual)}. Actual JSON written to '{actualPath}'.");
	}

	private static void Write(string path, string text)
	{
		var directory = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		File.WriteAllText(path, text, new UTF8Encoding(false));
	}

	private static string FirstDifference(string expected, string actual)
	{
		var e = expected.Split('\n');
		var a = actual.Split('\n');
		for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
		{
			var el = i < e.Length ? e[i] : "<end>";
			var al = i < a.Length ? a[i] : "<end>";
			if (el != al) return $"line {i + 1}: expected `{el.Trim()}`, actual `{al.Trim()}`";
		}

		return "whitespace differs";
	}

	private static JsonNode? NormalizeNode(JsonNode? node, JsonSnapshotOptions options)
	{
		switch (node)
		{
			case null:
				return null;
			case JsonObject obj:
			{
				var properties = obj.Where(p => !options.Ignore.Contains(p.Key)).Select(p => (p.Key, Value: NormalizeNode(p.Value, options)));
				if (options.SortProperties) properties = properties.OrderBy(static p => p.Key, StringComparer.Ordinal);
				var result = new JsonObject();
				foreach (var (key, value) in properties.ToList()) result[key] = value;
				return result;
			}
			case JsonArray array:
			{
				var result = new JsonArray();
				foreach (var item in array) result.Add(NormalizeNode(item, options));
				return result;
			}
			case JsonValue value when value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<double>(out var number):
			{
				var rounded = Math.Round(number, options.Decimals, MidpointRounding.AwayFromZero);
				if (rounded == 0) rounded = 0; // -0
				return rounded == Math.Floor(rounded) && Math.Abs(rounded) < 9e15 ? JsonValue.Create((long)rounded) : JsonValue.Create(rounded);
			}
			default:
				return node.DeepClone();
		}
	}
}

/// <summary>How <see cref="JsonSnapshot"/> normalizes JSON.</summary>
public sealed record JsonSnapshotOptions
{
	/// <summary>Three decimals, no ignored properties, properties in document order.</summary>
	public static JsonSnapshotOptions Default { get; } = new();

	/// <summary>The number of decimals numbers are rounded to.</summary>
	public int Decimals { get; init; } = JsonSnapshot.DefaultDecimals;

	/// <summary>Property names dropped at any depth.</summary>
	public IReadOnlySet<string> Ignore { get; init; } = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>Whether object properties are sorted by name.</summary>
	public bool SortProperties { get; init; }
}

/// <summary>A JSON snapshot did not match (or was missing).</summary>
public sealed class JsonSnapshotException(string message) : Exception(message);

/// <summary>
/// World snapshots: an ECS world as normalized JSON (the world serializer's format: every entity with its registered
/// components, entity references as positions), for <see cref="JsonSnapshot.AssertMatches"/>.
/// </summary>
public static class WorldSnapshot
{
	/// <summary>The world as normalized, indented JSON.</summary>
	/// <param name="world">The world.</param>
	/// <param name="registry">The components to save (the built-in ones when null).</param>
	/// <param name="decimals">The number of decimals numbers are rounded to.</param>
	public static string ToJson(World world, ComponentSerializerRegistry? registry = null, int decimals = JsonSnapshot.DefaultDecimals)
	{
		ArgumentNullException.ThrowIfNull(world);
		var serializer = new JsonWorldSerializer(registry ?? ComponentSerializerRegistry.CreateDefault());
		return JsonSnapshot.Normalize(serializer.ToJson(world), JsonSnapshotOptions.Default with { Decimals = decimals });
	}

	/// <summary>Asserts that <paramref name="world"/> matches the snapshot at <paramref name="snapshotPath"/> (see <see cref="JsonSnapshot"/>).</summary>
	public static string AssertMatches(World world, string snapshotPath, ComponentSerializerRegistry? registry = null, int decimals = JsonSnapshot.DefaultDecimals) =>
		JsonSnapshot.AssertMatches(ToJson(world, registry, decimals), snapshotPath, JsonSnapshotOptions.Default with { Decimals = decimals });
}
