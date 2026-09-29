using System.Runtime.CompilerServices;

namespace Ion.Extensions.Networking;

/// <summary>
/// A network message received from a peer, as it appears on the frame event bus (<see cref="IEvents"/>): every inbound
/// <see cref="NetworkMessageAttribute"/> message is emitted there, so coroutines and test collectors see it like any
/// other event. <see cref="NetworkReader{T}"/> reads the same channel.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
/// <param name="From">The sender.</param>
/// <param name="Tick">The sender's tick when it sent the message.</param>
/// <param name="Message">The message.</param>
public readonly record struct NetworkMessageReceived<T>(NetworkPeer From, uint Tick, T Message) where T : unmanaged;

/// <summary>
/// Typed network messages, mirroring <see cref="IEvents"/>: send with <see cref="Send{T}(NetworkPeer, in T)"/>,
/// <see cref="Broadcast{T}"/> or <see cref="SendToServer{T}"/>, read with a <see cref="NetworkReader{T}"/> created once (in
/// a constructor or field initializer, ION206 otherwise) and kept in a mutable field.
/// </summary>
/// <remarks>
/// Outbound messages are packed per peer and delivery and sent in <c>Last</c>; inbound ones are decoded in <c>First</c>
/// (before any game step) and are visible to readers for that frame and the next, like events, including in the frame's
/// fixed steps. A message is never boxed; its type travels as a small id assigned from the sorted type registry, so the
/// wire never carries type names. Sending does nothing while the session is not connected.
/// </remarks>
public interface INetworkMessages
{
	/// <summary>Sends <paramref name="message"/> to <paramref name="peer"/> with the delivery declared on its type.</summary>
	[SendsNetworkMessage]
	void Send<T>(NetworkPeer peer, in T message) where T : unmanaged;

	/// <summary>Sends <paramref name="message"/> to <paramref name="peer"/> with <paramref name="delivery"/>.</summary>
	[SendsNetworkMessage]
	void Send<T>(NetworkPeer peer, in T message, Delivery delivery) where T : unmanaged;

	/// <summary>On a server, sends <paramref name="message"/> to every connected client; on a client, to the server.</summary>
	[SendsNetworkMessage]
	void Broadcast<T>(in T message) where T : unmanaged;

	/// <summary>On a client, sends <paramref name="message"/> to the server; on a server (or a listen server) it is delivered locally.</summary>
	[SendsNetworkMessage]
	void SendToServer<T>(in T message) where T : unmanaged;

	/// <summary>Creates a reader of <typeparamref name="T"/> that starts at the oldest visible message. Call it once and keep it in a mutable field.</summary>
	[ReadsNetworkMessage]
	NetworkReader<T> Reader<T>() where T : unmanaged;
}

/// <summary>
/// A cursor over the inbound messages of one type (a plain struct over the event channel of
/// <see cref="NetworkMessageReceived{T}"/>): each reader sees each message once. Keep it in a field that is not
/// <see langword="readonly"/>.
/// </summary>
public struct NetworkReader<T> where T : unmanaged
{
	private EventReader<NetworkMessageReceived<T>> _reader;

	/// <summary>Wraps an event reader of <see cref="NetworkMessageReceived{T}"/>.</summary>
	public NetworkReader(EventReader<NetworkMessageReceived<T>> reader) => _reader = reader;

	/// <summary>The number of messages this reader has not read.</summary>
	public readonly int Count => _reader.Count;

	/// <summary>Whether there is a message this reader has not read.</summary>
	public readonly bool Any() => _reader.Any();

	/// <summary>Reads the oldest unread message and its sender.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryRead(out NetworkPeer from, out T message)
	{
		if (_reader.TryRead(out var received))
		{
			from = received.From;
			message = received.Message;
			return true;
		}

		from = NetworkPeer.None;
		message = default;
		return false;
	}

	/// <summary>Reads the oldest unread message with its sender and tick.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryRead(out NetworkMessageReceived<T> message) => _reader.TryRead(out message);

	/// <summary>Every unread message, oldest first (valid until the end of the frame).</summary>
	public ReadOnlySpan<NetworkMessageReceived<T>> Read() => _reader.Read();

	/// <summary>Marks every visible message as read.</summary>
	public void Skip() => _reader.Skip();
}
