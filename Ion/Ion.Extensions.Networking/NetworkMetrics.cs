using Ion.Extensions.Metrics;

namespace Ion.Extensions.Networking;

/// <summary>
/// The network counters on the metrics module (frame log, overlay, <c>dotnet-counters</c>): bytes and packets in and out,
/// messages, rejected, malformed and rate-limited input, handshake refusals, snapshots and their size, rewinds,
/// prediction corrections, the peer count and the round trip time. Updated once per frame by the send step, from the
/// session's <see cref="NetworkStats"/>; no allocation.
/// </summary>
internal sealed class NetworkMetrics
{
	private readonly MetricsCounter _bytesIn, _bytesOut, _packetsIn, _packetsOut, _messagesIn, _messagesOut;
	private readonly MetricsCounter _rejected, _malformed, _rateLimited, _handshakesRejected, _snapshots, _fullSnapshots;
	private readonly MetricsCounter _snapshotsLost, _rewinds, _rewindsRejected, _corrections, _resyncs;
	private readonly MetricsGauge _snapshotBytes, _peers, _rtt;
	private NetworkStats _last;

	public NetworkMetrics(IMetrics metrics)
	{
		_bytesIn = metrics.Counter("net_bytes_in", "By", "Bytes received");
		_bytesOut = metrics.Counter("net_bytes_out", "By", "Bytes sent");
		_packetsIn = metrics.Counter("net_packets_in", null, "Packets received");
		_packetsOut = metrics.Counter("net_packets_out", null, "Packets sent");
		_messagesIn = metrics.Counter("net_messages_in", null, "Network messages received");
		_messagesOut = metrics.Counter("net_messages_out", null, "Network messages sent");
		_rejected = metrics.Counter("net_rejected", null, "Unauthorized messages and mutations dropped");
		_malformed = metrics.Counter("net_malformed", null, "Malformed packets dropped");
		_rateLimited = metrics.Counter("net_rate_limited", null, "Packets and messages dropped by the rate limits");
		_handshakesRejected = metrics.Counter("net_handshakes_rejected", null, "Handshakes refused");
		_snapshots = metrics.Counter("net_snapshots", null, "Snapshots sent (server) or completed (client)");
		_fullSnapshots = metrics.Counter("net_full_snapshots", null, "Snapshots sent without a delta baseline");
		_snapshotsLost = metrics.Counter("net_snapshots_lost", null, "Snapshot ticks a client never completed");
		_rewinds = metrics.Counter("net_rewinds", null, "Lag compensation rewinds");
		_rewindsRejected = metrics.Counter("net_rewinds_rejected", null, "Lag compensation rewinds refused");
		_corrections = metrics.Counter("net_corrections", null, "Prediction corrections");
		_resyncs = metrics.Counter("net_resyncs", null, "Client tick resynchronizations");
		_snapshotBytes = metrics.Gauge("net_snapshot_bytes", "By", "Size of the last snapshot");
		_peers = metrics.Gauge("net_peers", null, "Connected peers");
		_rtt = metrics.Gauge("net_rtt_ms", "ms", "Round trip time (client: to the server; server: the mean over the clients)");
	}

	public void Update(NetworkSession session)
	{
		ref readonly var stats = ref session.Stats;
		Add(_bytesIn, stats.BytesIn, ref _last.BytesIn);
		Add(_bytesOut, stats.BytesOut, ref _last.BytesOut);
		Add(_packetsIn, stats.PacketsIn, ref _last.PacketsIn);
		Add(_packetsOut, stats.PacketsOut, ref _last.PacketsOut);
		Add(_messagesIn, stats.MessagesIn, ref _last.MessagesIn);
		Add(_messagesOut, stats.MessagesOut, ref _last.MessagesOut);
		Add(_rejected, stats.Rejected, ref _last.Rejected);
		Add(_malformed, stats.Malformed, ref _last.Malformed);
		Add(_rateLimited, stats.RateLimited, ref _last.RateLimited);
		Add(_handshakesRejected, stats.HandshakesRejected, ref _last.HandshakesRejected);
		Add(_snapshots, stats.Snapshots, ref _last.Snapshots);
		Add(_fullSnapshots, stats.FullSnapshots, ref _last.FullSnapshots);
		Add(_snapshotsLost, stats.SnapshotsLost, ref _last.SnapshotsLost);
		Add(_rewinds, stats.Rewinds, ref _last.Rewinds);
		Add(_rewindsRejected, stats.RewindsRejected, ref _last.RewindsRejected);
		Add(_corrections, stats.Corrections, ref _last.Corrections);
		Add(_resyncs, stats.Resyncs, ref _last.Resyncs);
		_snapshotBytes.Set(stats.LastSnapshotBytes);

		var peers = session.Peers;
		_peers.Set(peers.Count);
		var rtt = 0.0;
		for (var i = 0; i < peers.Count; i++) rtt += session.RoundTripTime(peers[i]).TotalMilliseconds;
		_rtt.Set(peers.Count == 0 ? 0 : rtt / peers.Count);
	}

	private static void Add(MetricsCounter counter, long value, ref long last)
	{
		if (value == last) return;
		counter.Add(value - last);
		last = value;
	}
}
