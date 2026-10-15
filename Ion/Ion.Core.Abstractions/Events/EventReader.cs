using System.Runtime.CompilerServices;

namespace Ion;

/// <summary>
/// A cursor over the channel of one event type. A plain struct (the channel and the position of the next unread event),
/// so it lives in a field of a system without allocating; each reader sees each event once.
/// </summary>
/// <remarks>
/// <para>
/// Keep a reader in a field that is not <see langword="readonly"/>: the read members advance the cursor, and on a
/// <see langword="readonly"/> field they would advance a copy (the generator reports ION106). Copying a reader copies its
/// position; the copies then advance independently.
/// </para>
/// <para>
/// Outside a FixedUpdate step a reader sees the events of the current and the previous frame. During a FixedUpdate step it
/// also sees older events that no fixed step has had a chance to see yet. A default reader (not created by
/// <see cref="IEvents.Reader{T}"/>) never has events.
/// </para>
/// </remarks>
public struct EventReader<T> where T : unmanaged
{
	private readonly EventChannel<T>? _channel;
	private long _cursor;

	internal EventReader(EventChannel<T> channel, long position)
	{
		_channel = channel;
		_cursor = position;
	}

	/// <summary>The channel this reader reads, or <see langword="null"/> for a default reader.</summary>
	public readonly EventChannel<T>? Channel => _channel;

	/// <summary>The sequence number of the next event this reader has not read.</summary>
	public readonly long Position => _cursor;

	/// <summary>The number of visible events this reader has not read.</summary>
	public readonly int Count
	{
		get
		{
			var channel = _channel;
			if (channel is null) return 0;
			var end = channel.EndSequence;
			var start = Math.Max(_cursor, channel.ReadStart);
			return start < end ? (int)(end - start) : 0;
		}
	}

	/// <summary>Whether there is a visible event this reader has not read. Does not advance the cursor.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly bool Any()
	{
		var channel = _channel;
		return channel is not null && Math.Max(_cursor, channel.ReadStart) < channel.EndSequence;
	}

	/// <summary>Reads the oldest unread event, if any, and advances past it.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryRead(out T e)
	{
		var channel = _channel;
		if (channel is not null)
		{
			var start = Math.Max(_cursor, channel.ReadStart);
			if (start < channel.EndSequence)
			{
				e = channel.At(start);
				_cursor = start + 1;
				return true;
			}
		}

		e = default;
		return false;
	}

	/// <summary>
	/// Every unread event, oldest first, and advances past them. The span is valid until the end of the frame (the bus's
	/// <see cref="EventBus.Step"/>); do not keep it longer.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ReadOnlySpan<T> Read()
	{
		var channel = _channel;
		if (channel is null) return default;

		var end = channel.EndSequence;
		var start = Math.Max(_cursor, channel.ReadStart);
		_cursor = end;
		return start < end ? channel.Slice(start, end) : default;
	}

	/// <summary>Reads every unread event and returns the newest, if there was one.</summary>
	public bool TryReadLatest(out T e)
	{
		var events = Read();
		if (events.IsEmpty)
		{
			e = default;
			return false;
		}

		e = events[^1];
		return true;
	}

	/// <summary>Marks every visible event as read without reading it.</summary>
	public void Skip()
	{
		if (_channel is not null) _cursor = _channel.EndSequence;
	}
}
