using System.ComponentModel;

namespace Ion.Extensions.Networking;

/// <summary>
/// The serializer of a replicated component or a network message, written by the networking generator: a full form and
/// a field-wise delta form (a change mask with one bit per serialized member, then only the changed members), an exact
/// (bitwise for floats) comparison and, for <see cref="InterpolatedAttribute"/> components, a blend.
/// </summary>
/// <typeparam name="T">The serialized type.</typeparam>
public abstract class NetSerializer<T> where T : unmanaged
{
	/// <summary>The largest size of the full form in bytes.</summary>
	public abstract int MaxSize { get; }

	/// <summary>Writes every serialized member of <paramref name="value"/>.</summary>
	public abstract void Write(ref NetWriter writer, in T value);

	/// <summary>Reads a value written by <see cref="Write"/> (members that are not serialized are default). Check <see cref="NetReader.Failed"/>.</summary>
	public abstract T Read(ref NetReader reader);

	/// <summary>
	/// Writes the members of <paramref name="value"/> that differ from <paramref name="baseline"/> (with their change mask)
	/// and returns true, or writes nothing and returns false when nothing differs.
	/// </summary>
	public abstract bool WriteDelta(ref NetWriter writer, in T baseline, in T value);

	/// <summary>Reads a delta written by <see cref="WriteDelta"/>: <paramref name="baseline"/> with the changed members. Check <see cref="NetReader.Failed"/>.</summary>
	public abstract T ReadDelta(ref NetReader reader, in T baseline);

	/// <summary>Whether the serialized members of the two values are identical (floats compared by their bits).</summary>
	public abstract bool Equal(in T a, in T b);

	/// <summary>
	/// The value a fraction <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/> (t may exceed 1
	/// to extrapolate). The generated override blends floats, doubles and vectors linearly and quaternions spherically;
	/// this default switches at the midpoint.
	/// </summary>
	public virtual T Interpolate(in T a, in T b, float t) => t < 0.5f ? a : b;
}

/// <summary>What a registered network type is.</summary>
public enum NetworkTypeKind : byte
{
	/// <summary>A replicated ECS component.</summary>
	Component = 1,

	/// <summary>A network message.</summary>
	Message = 2,
}

/// <summary>A registered network type: its name, its layout signature (from the generator) and its ordinal in the table.</summary>
public abstract class NetworkTypeInfo
{
	private protected NetworkTypeInfo(Type type, string name, string layout, NetworkTypeKind kind)
	{
		ArgumentNullException.ThrowIfNull(type);
		ArgumentException.ThrowIfNullOrEmpty(name);
		Type = type;
		Name = name;
		Layout = layout ?? "";
		Kind = kind;
	}

	/// <summary>The type.</summary>
	public Type Type { get; }

	/// <summary>The full name (the sort key of the table, the same in every build of the game).</summary>
	public string Name { get; }

	/// <summary>The layout signature: the serialized members and their types, as the generator saw them.</summary>
	public string Layout { get; }

	/// <summary>Whether it is a component or a message.</summary>
	public NetworkTypeKind Kind { get; }

	/// <summary>The index in the sorted table of its kind (the id on the wire), or -1 before the table is built.</summary>
	public int Ordinal { get; internal set; } = -1;

	/// <summary>The line this type contributes to the registry hash.</summary>
	internal abstract string Signature { get; }

	/// <inheritdoc/>
	public override string ToString() => $"{Kind} {Name} #{Ordinal}";
}

/// <summary>Creates something per replicated component type without reflection (see <see cref="ReplicatedTypeInfo.Accept"/>).</summary>
public interface IReplicatedTypeVisitor<out TResult>
{
	/// <summary>Called with the typed info.</summary>
	TResult Visit<T>(ReplicatedTypeInfo<T> info) where T : unmanaged;
}

/// <summary>A replicated component type.</summary>
public abstract class ReplicatedTypeInfo : NetworkTypeInfo
{
	private protected ReplicatedTypeInfo(Type type, string name, string layout, Authority authority, bool predicted, bool interpolated)
		: base(type, name, layout, NetworkTypeKind.Component)
	{
		Authority = authority;
		Predicted = predicted;
		Interpolated = interpolated;
	}

	/// <summary>Who writes it.</summary>
	public Authority Authority { get; }

	/// <summary>Whether clients predict it for their own entities.</summary>
	public bool Predicted { get; }

	/// <summary>Whether clients interpolate it for remote entities.</summary>
	public bool Interpolated { get; }

	/// <summary>Calls <paramref name="visitor"/> with the typed info.</summary>
	public abstract TResult Accept<TResult>(IReplicatedTypeVisitor<TResult> visitor);

	internal override string Signature => $"C|{Name}|{Layout}|{(int)Authority}|{(Predicted ? 1 : 0)}|{(Interpolated ? 1 : 0)}";
}

/// <summary>A replicated component type <typeparamref name="T"/>.</summary>
public sealed class ReplicatedTypeInfo<T> : ReplicatedTypeInfo where T : unmanaged
{
	/// <summary>Describes <typeparamref name="T"/>. Called by the generated registration.</summary>
	public ReplicatedTypeInfo(string name, string layout, NetSerializer<T> serializer, Authority authority = Authority.Server, bool predicted = false, bool interpolated = false)
		: base(typeof(T), name, layout, authority, predicted, interpolated)
	{
		ArgumentNullException.ThrowIfNull(serializer);
		Serializer = serializer;
	}

	/// <summary>The generated serializer.</summary>
	public NetSerializer<T> Serializer { get; }

	/// <inheritdoc/>
	public override TResult Accept<TResult>(IReplicatedTypeVisitor<TResult> visitor)
	{
		ArgumentNullException.ThrowIfNull(visitor);
		return visitor.Visit(this);
	}
}

/// <summary>A network message type.</summary>
public abstract class MessageTypeInfo : NetworkTypeInfo
{
	private protected MessageTypeInfo(Type type, string name, string layout, Delivery delivery, MessageDirection direction)
		: base(type, name, layout, NetworkTypeKind.Message)
	{
		Delivery = delivery;
		Direction = direction;
	}

	/// <summary>The declared delivery.</summary>
	public Delivery Delivery { get; }

	/// <summary>Who may send it.</summary>
	public MessageDirection Direction { get; }

	/// <summary>Whether a client may send it.</summary>
	public bool ClientMaySend => Direction is MessageDirection.ClientToServer or MessageDirection.Both;

	/// <summary>Whether the server may send it.</summary>
	public bool ServerMaySend => Direction is MessageDirection.ServerToClient or MessageDirection.Both;

	/// <summary>
	/// Decodes one message from <paramref name="reader"/> and emits it on <paramref name="events"/> as a
	/// <see cref="NetworkMessageReceived{T}"/>. Returns false (emitting nothing) when the payload is malformed.
	/// </summary>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public abstract bool Dispatch(ref NetReader reader, NetworkPeer from, uint tick, IEvents events);

	internal override string Signature => $"M|{Name}|{Layout}|{(int)Delivery}|{(int)Direction}";
}

/// <summary>A network message type <typeparamref name="T"/>.</summary>
public sealed class MessageTypeInfo<T> : MessageTypeInfo where T : unmanaged
{
	/// <summary>Describes <typeparamref name="T"/>. Called by the generated registration.</summary>
	public MessageTypeInfo(string name, string layout, NetSerializer<T> serializer, Delivery delivery = Delivery.ReliableOrdered, MessageDirection direction = MessageDirection.ServerToClient)
		: base(typeof(T), name, layout, delivery, direction)
	{
		ArgumentNullException.ThrowIfNull(serializer);
		Serializer = serializer;
	}

	/// <summary>The generated serializer.</summary>
	public NetSerializer<T> Serializer { get; }

	/// <inheritdoc/>
	public override bool Dispatch(ref NetReader reader, NetworkPeer from, uint tick, IEvents events)
	{
		var message = Serializer.Read(ref reader);
		if (reader.Failed) return false;
		events.Emit(new NetworkMessageReceived<T>(from, tick, message));
		return true;
	}
}
