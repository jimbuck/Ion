using System.Text;

namespace Ion.Extensions.Networking;

/// <summary>The registered info of <typeparamref name="T"/>, cached per type (no dictionary lookup on the hot paths).</summary>
public static class NetworkTypes<T> where T : unmanaged
{
	/// <summary>The info when <typeparamref name="T"/> is a registered replicated component.</summary>
	public static ReplicatedTypeInfo<T>? Component { get; internal set; }

	/// <summary>The info when <typeparamref name="T"/> is a registered network message.</summary>
	public static MessageTypeInfo<T>? Message { get; internal set; }
}

/// <summary>
/// The process-wide registry of replicated components and network messages. The networking generator registers every
/// type it serializes from a module initializer of the assembly that declares it (and the application's generated
/// registration calls those of its references), so the table is complete before the first connection.
/// </summary>
public static class NetworkRegistry
{
	private static readonly Lock Gate = new();
	private static readonly List<NetworkTypeInfo> Types = [];
	private static NetworkTypeTable? _table;

	/// <summary>Registers a replicated component (a second registration of the same type is ignored).</summary>
	public static void Register<T>(ReplicatedTypeInfo<T> info) where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull(info);
		lock (Gate)
		{
			if (NetworkTypes<T>.Component is not null) return;
			NetworkTypes<T>.Component = info;
			Types.Add(info);
			_table = null;
		}
	}

	/// <summary>Registers a network message (a second registration of the same type is ignored).</summary>
	public static void Register<T>(MessageTypeInfo<T> info) where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull(info);
		lock (Gate)
		{
			if (NetworkTypes<T>.Message is not null) return;
			NetworkTypes<T>.Message = info;
			Types.Add(info);
			_table = null;
		}
	}

	/// <summary>
	/// The table of every registered type, sorted by full name, with ordinals and the registry hash. Rebuilt when a type
	/// is registered after it was built (assign the ordinals before starting a session).
	/// </summary>
	public static NetworkTypeTable Table
	{
		get
		{
			var table = _table;
			if (table is not null) return table;
			lock (Gate)
			{
				return _table ??= NetworkTypeTable.Create(Types, assignOrdinals: true);
			}
		}
	}
}

/// <summary>
/// A sorted, numbered set of network types: components and messages each sorted by full name (ordinal) so that every
/// build of the same game agrees, and a 64-bit FNV-1a hash of every type's signature (name, layout, authority, delivery),
/// exchanged in the handshake.
/// </summary>
public sealed class NetworkTypeTable
{
	/// <summary>The protocol version, part of the hash and of the handshake.</summary>
	public const ushort ProtocolVersion = 1;

	private readonly ReplicatedTypeInfo[] _components;
	private readonly MessageTypeInfo[] _messages;

	private NetworkTypeTable(ReplicatedTypeInfo[] components, MessageTypeInfo[] messages, ulong hash)
	{
		_components = components;
		_messages = messages;
		Hash = hash;
	}

	/// <summary>The replicated components, by ordinal.</summary>
	public IReadOnlyList<ReplicatedTypeInfo> Components => _components;

	/// <summary>The network messages, by ordinal.</summary>
	public IReadOnlyList<MessageTypeInfo> Messages => _messages;

	/// <summary>The registry hash.</summary>
	public ulong Hash { get; }

	/// <summary>The message with <paramref name="ordinal"/>, or null.</summary>
	public MessageTypeInfo? Message(uint ordinal) => ordinal < (uint)_messages.Length ? _messages[ordinal] : null;

	/// <summary>The component with <paramref name="ordinal"/>, or null.</summary>
	public ReplicatedTypeInfo? Component(uint ordinal) => ordinal < (uint)_components.Length ? _components[ordinal] : null;

	/// <summary>
	/// Builds a table from <paramref name="types"/> (in any order). With <paramref name="assignOrdinals"/> the types'
	/// <see cref="NetworkTypeInfo.Ordinal"/> is set (the process registry does this; a table built for a comparison does not).
	/// </summary>
	public static NetworkTypeTable Create(IEnumerable<NetworkTypeInfo> types, bool assignOrdinals = false)
	{
		ArgumentNullException.ThrowIfNull(types);
		var components = types.OfType<ReplicatedTypeInfo>().OrderBy(static t => t.Name, StringComparer.Ordinal).ToArray();
		var messages = types.OfType<MessageTypeInfo>().OrderBy(static t => t.Name, StringComparer.Ordinal).ToArray();

		if (assignOrdinals)
		{
			for (var i = 0; i < components.Length; i++) components[i].Ordinal = i;
			for (var i = 0; i < messages.Length; i++) messages[i].Ordinal = i;
		}

		var text = new StringBuilder();
		text.Append("ion-net/").Append(ProtocolVersion).Append('\n');
		foreach (var component in components) text.Append(component.Signature).Append('\n');
		foreach (var message in messages) text.Append(message.Signature).Append('\n');
		return new NetworkTypeTable(components, messages, Fnv1a64(Encoding.UTF8.GetBytes(text.ToString())));
	}

	/// <summary>The 64-bit FNV-1a hash of <paramref name="bytes"/>.</summary>
	public static ulong Fnv1a64(ReadOnlySpan<byte> bytes)
	{
		var hash = 14695981039346656037UL;
		foreach (var b in bytes)
		{
			hash ^= b;
			hash *= 1099511628211UL;
		}

		return hash;
	}

	/// <summary>The 64-bit FNV-1a hash of the UTF-8 bytes of <paramref name="text"/>.</summary>
	public static ulong Fnv1a64(string text) => Fnv1a64(Encoding.UTF8.GetBytes(text ?? ""));
}
