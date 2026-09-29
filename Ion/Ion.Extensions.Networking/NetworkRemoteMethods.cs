using System.Text.Json.Nodes;

using Ion.Extensions.Remote;

namespace Ion.Extensions.Networking;

/// <summary>
/// The networking module's remote method, <c>network.status</c> (read, watchable): the role and state of the session,
/// the ticks, the peers with their round trip times, and the counters. Registered by <c>AddNetworking</c> through
/// <see cref="IRemoteMethodProvider"/>, so the remote server needs no reference to the networking module.
/// </summary>
internal sealed class NetworkRemoteMethods(NetworkSession session) : IRemoteMethodProvider
{
	public void Register(RemoteMethodRegistry methods) =>
		methods.Read("network.status", "The network session: mode, state, local peer, ticks, peers with round trip times, and the traffic, security and prediction counters.", Status, watchable: true);

	private JsonNode Status(RemoteRequest request)
	{
		var peers = new JsonArray();
		foreach (var peer in session.Peers)
		{
			peers.Add((JsonNode)new JsonObject
			{
				["id"] = peer.Id,
				["name"] = peer.ToString(),
				["rttMs"] = session.RoundTripTime(peer).TotalMilliseconds,
				["ackedTick"] = session.World.LastAckedTick(peer),
			});
		}

		ref readonly var stats = ref session.Stats;
		return new JsonObject
		{
			["mode"] = session.Mode.ToString(),
			["state"] = session.State.ToString(),
			["transport"] = session.Transport?.Name,
			["localPeer"] = session.LocalPeer.Id,
			["disconnectReason"] = session.DisconnectReason.ToString(),
			["tickRate"] = session.TickRate,
			["tick"] = session.World.CurrentTick,
			["lastReceivedServerTick"] = session.World.LastReceivedServerTick,
			["registryHash"] = session.Table.Hash.ToString("X16", System.Globalization.CultureInfo.InvariantCulture),
			["peers"] = peers,
			["stats"] = new JsonObject
			{
				["bytesIn"] = stats.BytesIn,
				["bytesOut"] = stats.BytesOut,
				["packetsIn"] = stats.PacketsIn,
				["packetsOut"] = stats.PacketsOut,
				["messagesIn"] = stats.MessagesIn,
				["messagesOut"] = stats.MessagesOut,
				["rejected"] = stats.Rejected,
				["malformed"] = stats.Malformed,
				["rateLimited"] = stats.RateLimited,
				["handshakesRejected"] = stats.HandshakesRejected,
				["snapshots"] = stats.Snapshots,
				["fullSnapshots"] = stats.FullSnapshots,
				["snapshotsLost"] = stats.SnapshotsLost,
				["lastSnapshotBytes"] = stats.LastSnapshotBytes,
				["rewinds"] = stats.Rewinds,
				["rewindsRejected"] = stats.RewindsRejected,
				["corrections"] = stats.Corrections,
				["resyncs"] = stats.Resyncs,
			},
		};
	}
}
