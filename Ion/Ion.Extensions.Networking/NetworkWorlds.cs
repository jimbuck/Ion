using System.Diagnostics.CodeAnalysis;

using Arch.Core;

namespace Ion.Extensions.Networking;

/// <summary>
/// The networked worlds of a <see cref="NetworkSession"/> (<see cref="INetworkWorlds"/>): the root world (index 0) and the
/// named worlds of the loaded scenes, each replicated by its own <see cref="NetworkWorld"/>. Names are registered before
/// the session starts (a scene's <c>UseNetworking("name")</c> registers its name when the application's schedule is
/// built) and numbered in ordinal name order, so both sides agree on the indices.
/// </summary>
public sealed class NetworkWorlds : INetworkWorlds
{
	/// <summary>The most named worlds (indices 1 to 255).</summary>
	public const int MaxNamedWorlds = 255;

	private readonly NetworkSession _session;
	private readonly List<string> _names = [];
	private readonly NetworkWorld?[] _byIndex = new NetworkWorld?[256];
	private readonly List<NetworkWorld> _live = [];
	private readonly byte[] _epochs = new byte[256];
	private bool _frozen;

	internal NetworkWorlds(NetworkSession session, NetworkWorld root)
	{
		_session = session;
		Root = root;
		_byIndex[0] = root;
		_live.Add(root);
	}

	/// <summary>The root world's replication.</summary>
	public NetworkWorld Root { get; }

	INetworkWorld INetworkWorlds.Root => Root;

	/// <inheritdoc/>
	public IReadOnlyList<INetworkWorld> Live => _live;

	/// <summary>The live worlds, root first.</summary>
	internal List<NetworkWorld> LiveWorlds => _live;

	/// <inheritdoc/>
	public IReadOnlyList<string> Names => _names;

	/// <summary>
	/// Registers a world name (both sides must register the same set). Registering a name twice does nothing; a new name
	/// after the session started throws, because the handshake already used the set.
	/// </summary>
	public void Register(string name)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		var at = _names.BinarySearch(name, StringComparer.Ordinal);
		if (at >= 0) return;
		if (_frozen) throw new InvalidOperationException($"The networked world '{name}' was registered after the session started. Scenes register their world names when the application's schedule is built: call scene.UseNetworking(\"{name}\") unconditionally in the scene's configuration.");
		if (_names.Count >= MaxNamedWorlds) throw new InvalidOperationException($"More than {MaxNamedWorlds} networked world names.");
		_names.Insert(~at, name);
	}

	/// <summary>The index of the world named <paramref name="name"/> (0 for the empty name, the root).</summary>
	/// <exception cref="ArgumentException">The name is not registered.</exception>
	public byte IndexOf(string name)
	{
		if (string.IsNullOrEmpty(name)) return 0;
		var at = _names.BinarySearch(name, StringComparer.Ordinal);
		return at >= 0 ? (byte)(at + 1) : throw new ArgumentException($"'{name}' is not a registered networked world.", nameof(name));
	}

	/// <summary>The name of the world with <paramref name="index"/> (empty for the root), or null.</summary>
	internal string? NameOf(byte index) => index == 0 ? "" : index <= _names.Count ? _names[index - 1] : null;

	/// <summary>Whether <paramref name="index"/> is a registered named world.</summary>
	internal bool IsNamed(byte index) => index >= 1 && index <= _names.Count;

	/// <summary>The session started: the names are fixed.</summary>
	internal void Freeze() => _frozen = true;

	/// <summary>The registry hash of the handshake: the type table's, combined with the world names when there are any.</summary>
	internal ulong Hash(ulong tableHash) => _names.Count == 0
		? tableHash
		: NetworkTypeTable.Fnv1a64($"{tableHash:X16}\nworlds\n{string.Join('\n', _names)}");

	/// <summary>The live world with <paramref name="index"/>, or null.</summary>
	internal NetworkWorld? Get(byte index) => _byIndex[index];

	/// <summary>
	/// Starts replicating <paramref name="world"/> as the named world <paramref name="name"/> (registering the name if the
	/// session has not started). A client asks the server for it; the server sends it to the clients that asked.
	/// </summary>
	/// <exception cref="InvalidOperationException">A world with that name is already live.</exception>
	public NetworkWorld Attach(string name, World world)
	{
		ArgumentNullException.ThrowIfNull(world);
		Register(name);
		var index = IndexOf(name);
		if (_byIndex[index] is not null) throw new InvalidOperationException($"The networked world '{name}' is already live: one world at a time is networked under a name.");

		// A client numbers its opens of a world, so the server's snapshots of an earlier open are told apart.
		byte epoch = 0;
		if (_session.IsClient)
		{
			epoch = (byte)(_epochs[index] + 1);
			if (epoch == 0) epoch = 1;
			_epochs[index] = epoch;
		}

		var network = new NetworkWorld(_session, world, _session.Config, _session.Table, name, index, epoch);
		_byIndex[index] = network;
		_live.Add(network);
		_session.OnWorldAttached(network);
		return network;
	}

	/// <summary>Stops replicating a named world (its scene unloaded). The root world is never detached.</summary>
	public void Detach(NetworkWorld world)
	{
		ArgumentNullException.ThrowIfNull(world);
		if (world.Index == 0 || _byIndex[world.Index] != world) return;
		_byIndex[world.Index] = null;
		_live.Remove(world);
		world.IsDetached = true;
		_session.OnWorldDetached(world);
	}

	/// <inheritdoc/>
	public bool TryGet(byte index, [NotNullWhen(true)] out INetworkWorld? world)
	{
		world = _byIndex[index];
		return world is not null;
	}

	/// <inheritdoc/>
	public bool TryGet(string name, [NotNullWhen(true)] out INetworkWorld? world)
	{
		world = null;
		if (string.IsNullOrEmpty(name))
		{
			world = Root;
			return true;
		}

		var at = _names.BinarySearch(name, StringComparer.Ordinal);
		if (at >= 0) world = _byIndex[at + 1];
		return world is not null;
	}

	/// <inheritdoc/>
	public bool TryGetEntity(NetworkId id, out World world, out Entity entity)
	{
		if (_byIndex[id.World] is { } network && network.TryGetEntity(id, out entity))
		{
			world = network.World;
			return true;
		}

		world = null!;
		entity = Entity.Null;
		return false;
	}
}
