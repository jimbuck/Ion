namespace Ion.Extensions.Networking;

/// <summary>The role of the process in a network session.</summary>
public enum NetworkMode
{
	/// <summary>No networking: the steps do nothing and no transport is started (the default).</summary>
	Offline = 0,

	/// <summary>Connects to a server (<see cref="NetworkConfig.Connect"/>).</summary>
	Client = 1,

	/// <summary>A dedicated server: listens on <see cref="NetworkConfig.Bind"/>:<see cref="NetworkConfig.Port"/>.</summary>
	Server = 2,

	/// <summary>A server that is also a player: listens like <see cref="Server"/>, and its own entities (owned by <see cref="NetworkPeer.Server"/>) take local input.</summary>
	ListenServer = 3,
}

/// <summary>
/// The networking settings, bound from <c>Ion:Network</c> (for a dedicated server:
/// <c>--Ion:Headless=true --Ion:Network:Mode=Server</c>) and adjustable with <c>AddNetworking(configure: ...)</c>.
/// </summary>
public sealed class NetworkConfig
{
	/// <summary>The configuration section: <c>Ion:Network</c>.</summary>
	public const string Section = "Ion:Network";

	/// <summary>The role (<see cref="NetworkMode.Offline"/> by default).</summary>
	public NetworkMode Mode { get; set; }

	/// <summary>
	/// The address a server listens on. Loopback (<c>127.0.0.1</c>) by default; any other address is logged as a public
	/// bind when the server starts.
	/// </summary>
	public string Bind { get; set; } = "127.0.0.1";

	/// <summary>The port a server listens on, and the port a client connects to (0 on a server: any free port).</summary>
	public int Port { get; set; } = 7777;

	/// <summary>The server address a client connects to.</summary>
	public string Connect { get; set; } = "127.0.0.1";

	/// <summary>
	/// The simulation rate in ticks per second; 0 (the default) takes <c>GameConfig.FixedUpdateRate</c>. Exchanged in the
	/// handshake: a client with a different rate is refused.
	/// </summary>
	public double TickRate { get; set; }

	/// <summary>The number of ticks the snapshot ring keeps (128 by default): the reach of delta baselines, interpolation and lag compensation.</summary>
	public int SnapshotHistory { get; set; } = 128;

	/// <summary>Snapshots per second the server sends to each client; 0 (the default) sends one per tick.</summary>
	public double SendRate { get; set; }

	/// <summary>The most clients a server accepts (at most 254).</summary>
	public int MaxPeers { get; set; } = 16;

	/// <summary>A game identifier compared in the handshake; empty (the default) uses <c>GameConfig.Title</c>.</summary>
	public string GameId { get; set; } = "";

	/// <summary>
	/// When set, a client must prove it knows this secret to join: the server sends a random challenge and the client
	/// answers with an HMAC-SHA256 of it keyed with the secret, so the secret itself never crosses the network.
	/// </summary>
	public string? JoinSecret { get; set; }

	/// <summary>The furthest back (in ticks) lag compensation rewinds (12 by default).</summary>
	public int MaxRewindTicks { get; set; } = 12;

	/// <summary>How many ticks behind the newest received snapshot a client draws remote entities (2 by default).</summary>
	public double InterpolationDelay { get; set; } = 2;

	/// <summary>How many ticks a client extrapolates a remote entity past the newest snapshot before it freezes it (2 by default).</summary>
	public double MaxExtrapolationTicks { get; set; } = 2;

	/// <summary>How many ticks ahead of its estimate of the server tick a client runs, so its inputs arrive before the server needs them (2 by default).</summary>
	public int InputLeadTicks { get; set; } = 2;

	/// <summary>The largest packet sent (1200 bytes by default, below every common path MTU). Snapshots are split into parts of this size.</summary>
	public int MtuBytes { get; set; } = 1200;

	/// <summary>A peer that sends nothing for this long is disconnected (10 seconds by default).</summary>
	public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(10);

	/// <summary>A connection whose handshake has not completed after this long is closed (5 seconds by default).</summary>
	public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(5);

	/// <summary>How often each side measures the round trip time (250 ms by default).</summary>
	public TimeSpan PingInterval { get; set; } = TimeSpan.FromMilliseconds(250);

	/// <summary>The most messages a client may send per second before the server drops them (600 by default).</summary>
	public int MaxMessagesPerSecond { get; set; } = 600;

	/// <summary>The most bytes a client may send per second before the server drops its packets (262144 by default).</summary>
	public int MaxBytesPerSecond { get; set; } = 256 * 1024;

	/// <summary>The protocol violations (unauthorized messages or mutations, malformed or rate-limited packets) after which a client is disconnected (50 by default).</summary>
	public int MaxViolations { get; set; } = 50;

	/// <summary>A simulated network for local testing: latency, jitter, loss and reordering (the loopback transport always applies it).</summary>
	public NetworkSimulation Simulate { get; set; } = new();
}

/// <summary>A simulated network (see <see cref="NetworkConfig.Simulate"/>).</summary>
public sealed class NetworkSimulation
{
	/// <summary>The one-way delay added to every packet.</summary>
	public TimeSpan Latency { get; set; }

	/// <summary>A random extra delay between 0 and this, per packet.</summary>
	public TimeSpan Jitter { get; set; }

	/// <summary>The probability (0 to 1) that an unreliable or sequenced packet is dropped.</summary>
	public double Loss { get; set; }

	/// <summary>The probability (0 to 1) that an unreliable packet is held back by one more latency, so later packets overtake it.</summary>
	public double Reorder { get; set; }

	/// <summary>The seed of the random decisions, so a simulated run is repeatable.</summary>
	public int Seed { get; set; } = 1;

	/// <summary>Whether anything is simulated.</summary>
	public bool IsActive => Latency > TimeSpan.Zero || Jitter > TimeSpan.Zero || Loss > 0 || Reorder > 0;
}
