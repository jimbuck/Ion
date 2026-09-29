using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ion.Extensions.Remote;

/// <summary>Builds JSON-RPC 2.0 response messages (UTF-8, no trailing newline).</summary>
internal static class RemoteMessages
{
	public static byte[] Result(JsonNode? id, JsonNode? result)
	{
		var buffer = new ArrayBufferWriter<byte>(256);
		using (var writer = new Utf8JsonWriter(buffer))
		{
			writer.WriteStartObject();
			writer.WriteString("jsonrpc"u8, "2.0");
			writer.WritePropertyName("id"u8);
			WriteNode(writer, id);
			writer.WritePropertyName("result"u8);
			WriteNode(writer, result);
			writer.WriteEndObject();
		}

		return buffer.WrittenSpan.ToArray();
	}

	public static byte[] Error(JsonNode? id, int code, string message, JsonNode? data = null)
	{
		var buffer = new ArrayBufferWriter<byte>(256);
		using (var writer = new Utf8JsonWriter(buffer))
		{
			writer.WriteStartObject();
			writer.WriteString("jsonrpc"u8, "2.0");
			writer.WritePropertyName("id"u8);
			WriteNode(writer, id);
			writer.WritePropertyName("error"u8);
			writer.WriteStartObject();
			writer.WriteNumber("code"u8, code);
			writer.WriteString("message"u8, message);
			if (data is not null)
			{
				writer.WritePropertyName("data"u8);
				data.WriteTo(writer);
			}

			writer.WriteEndObject();
			writer.WriteEndObject();
		}

		return buffer.WrittenSpan.ToArray();
	}

	private static void WriteNode(Utf8JsonWriter writer, JsonNode? node)
	{
		if (node is null) writer.WriteNullValue();
		else node.WriteTo(writer);
	}
}

/// <summary>What the token file holds (see <see cref="RemoteServer"/>).</summary>
public sealed class RemoteEndpointInfo
{
	/// <summary>The token file format version.</summary>
	public int Version { get; set; } = 1;

	/// <summary>The game's process id.</summary>
	public int Pid { get; set; }

	/// <summary>The game's title (<c>Ion:Title</c>).</summary>
	public string? Title { get; set; }

	/// <summary>The HTTP JSON-RPC endpoint (POST), or null when only stdio runs.</summary>
	public string? Url { get; set; }

	/// <summary>The WebSocket endpoint, or null when only stdio runs.</summary>
	public string? WebSocketUrl { get; set; }

	/// <summary>The bearer token of the read scope.</summary>
	public string? ReadToken { get; set; }

	/// <summary>The bearer token of the mutate scope (which includes read), or null when mutations are not allowed.</summary>
	public string? MutateToken { get; set; }

	/// <summary>Whether mutations are allowed.</summary>
	public bool AllowMutations { get; set; }

	/// <summary>When the server started (UTC, ISO 8601).</summary>
	public string? StartedAt { get; set; }
}

/// <summary>Source-generated JSON metadata for the remote module's typed documents (NativeAOT-safe).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RemoteEndpointInfo))]
[JsonSerializable(typeof(GameTimeInfo))]
[JsonSerializable(typeof(bool))]
internal sealed partial class RemoteJsonContext : JsonSerializerContext
{
}

/// <summary>The <c>Ion.GameTime</c> resource.</summary>
internal sealed record GameTimeInfo(uint Frame, double ElapsedSeconds, float Delta, long FixedSteps, string Stage);
