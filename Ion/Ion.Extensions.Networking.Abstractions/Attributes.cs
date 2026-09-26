namespace Ion.Extensions.Networking;

/// <summary>Who may write a replicated component.</summary>
public enum Authority : byte
{
	/// <summary>The server writes the component; clients only receive it (the default).</summary>
	Server = 0,

	/// <summary>
	/// The owner of the entity (<see cref="NetworkId.OwnerPeer"/>) writes the component: the owning client sends its
	/// values to the server, which checks the ownership and forwards them to every other peer. For an entity the server
	/// owns this is the same as <see cref="Server"/>. A <see cref="PredictedAttribute"/> component is instead simulated on
	/// both sides from the owner's inputs (see <see cref="INetworkPrediction"/>).
	/// </summary>
	Owner = 1,
}

/// <summary>How a network message is delivered.</summary>
public enum Delivery : byte
{
	/// <summary>May be lost, duplicated by nothing but the network, or arrive out of order. Cheapest; for inputs sent every tick.</summary>
	Unreliable = 0,

	/// <summary>May be lost; never arrives after a newer message of the same channel (older ones are dropped).</summary>
	Sequenced = 1,

	/// <summary>Always arrives (resent until acknowledged), in any order.</summary>
	ReliableUnordered = 2,

	/// <summary>Always arrives, in the order it was sent. For chat, match state and commands.</summary>
	ReliableOrdered = 3,
}

/// <summary>Who may send a network message. The server drops (and counts) a message a client may not send.</summary>
public enum MessageDirection : byte
{
	/// <summary>Only the server sends it (the default): a client that sends it violates the protocol.</summary>
	ServerToClient = 0,

	/// <summary>Only clients send it (inputs, requests).</summary>
	ClientToServer = 1,

	/// <summary>Both sides send it.</summary>
	Both = 2,
}

/// <summary>
/// Marks an ECS component whose value is replicated from the server (or the entity's owner, see <see cref="Authority"/>)
/// to every client. The networking generator (<c>Ion.Extensions.Networking.Generators</c>) writes a full and a field-wise
/// delta serializer for it and registers it; every entity with a replicated component gets a <see cref="NetworkId"/> on
/// the server.
/// </summary>
/// <remarks>
/// The component must be <see langword="unmanaged"/> (ION201) and every serialized member must be a supported type:
/// primitives, enums, <c>System.Numerics</c> vectors and quaternions, <see cref="FixedString32"/>,
/// <see cref="FixedString64"/>, <see cref="FixedString128"/>, <see cref="NetworkId"/>, or a struct of those (ION205).
/// Members marked <see cref="NetworkIgnoreAttribute"/> are not sent.
/// </remarks>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class ReplicatedAttribute : Attribute
{
	/// <summary>Who writes the component (<see cref="Networking.Authority.Server"/> by default).</summary>
	public Authority Authority { get; set; }
}

/// <summary>
/// Marks a replicated component that clients predict for the entities they own: the client applies its inputs to the
/// component immediately (<see cref="INetworkPrediction"/>), and corrects it when the server's value for the same tick
/// differs (reconciliation). Needs <see cref="ReplicatedAttribute"/> with <see cref="Authority.Owner"/> (ION202).
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class PredictedAttribute : Attribute;

/// <summary>
/// Marks a replicated component that clients interpolate on the entities they do not own: the value drawn is blended
/// between the two received snapshots around the interpolation time (<see cref="NetworkConfig.InterpolationDelay"/>
/// behind the newest). The generator writes the blend: floats, doubles and vectors interpolate linearly, quaternions
/// spherically, anything else switches at the midpoint. Needs <see cref="ReplicatedAttribute"/> (ION209).
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class InterpolatedAttribute : Attribute;

/// <summary>A member of a replicated component or a network message that is not sent (it keeps its previous value on the receiver).</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class NetworkIgnoreAttribute : Attribute;

/// <summary>
/// Replicates a component type declared elsewhere (for example the ECS module's <c>Transform2D</c>), as if it were marked
/// <see cref="ReplicatedAttribute"/>: the networking generator writes its serializers into this assembly. Its serialized
/// members must be public and writable.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ReplicateComponentAttribute(Type componentType) : Attribute
{
	/// <summary>The component type.</summary>
	public Type ComponentType { get; } = componentType;

	/// <summary>Who writes the component.</summary>
	public Authority Authority { get; set; }

	/// <summary>Whether clients predict it for their own entities (see <see cref="PredictedAttribute"/>).</summary>
	public bool Predicted { get; set; }

	/// <summary>Whether clients interpolate it for the entities they do not own (see <see cref="InterpolatedAttribute"/>).</summary>
	public bool Interpolated { get; set; }
}

/// <summary>
/// Marks an <see langword="unmanaged"/> struct sent with <see cref="INetworkMessages"/> and read with
/// <see cref="NetworkReader{T}"/>. The networking generator writes its serializer and gives it a stable id; the wire never
/// carries type names.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class NetworkMessageAttribute : Attribute
{
	/// <summary>How the message is delivered (<see cref="Networking.Delivery.ReliableOrdered"/> by default).</summary>
	public Delivery Delivery { get; set; } = Delivery.ReliableOrdered;

	/// <summary>Who may send it (<see cref="MessageDirection.ServerToClient"/> by default).</summary>
	public MessageDirection Direction { get; set; } = MessageDirection.ServerToClient;
}

/// <summary>
/// Marks a method that sends a network message, so the networking generator counts its call sites as sends (ION207,
/// ION208): of the method's type argument at <see cref="TypeArgument"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SendsNetworkMessageAttribute(int typeArgument = 0) : Attribute
{
	/// <summary>The index of the method's type argument that is the message type.</summary>
	public int TypeArgument { get; } = typeArgument;
}

/// <summary>
/// Marks a method that reads (or creates a reader of) a network message, so the networking generator counts its call
/// sites as reads: of the method's type argument at <see cref="TypeArgument"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ReadsNetworkMessageAttribute(int typeArgument = 0) : Attribute
{
	/// <summary>The index of the method's type argument that is the message type.</summary>
	public int TypeArgument { get; } = typeArgument;
}

/// <summary>
/// Written by the networking generator into an assembly that declares replicated components or messages: the generated
/// class whose <c>Register()</c> method registers them. An application's generated registration calls the registration of
/// every referenced assembly that has one, so the types are known before the first connection.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class NetworkTypesAssemblyAttribute(Type registration) : Attribute
{
	/// <summary>The generated registration class.</summary>
	public Type Registration { get; } = registration;
}
