namespace Ion.Tests;

public class EventTests
{
	public record struct TestEvent(int Value);
	public record struct OtherEvent(long Value);

	[Fact, Trait(CATEGORY, UNIT)]
	public void AReaderSeesAnEventOnce()
	{
		var bus = new EventBus();
		var reader = bus.Reader<TestEvent>();

		bus.Emit(new TestEvent(1));

		Assert.True(reader.Any());
		Assert.True(reader.TryRead(out var e));
		Assert.Equal(1, e.Value);

		Assert.False(reader.Any());
		Assert.False(reader.TryRead(out _));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TwoReadersEachSeeEveryEventOnce()
	{
		var bus = new EventBus();
		var first = bus.Reader<TestEvent>();
		var second = bus.Reader<TestEvent>();

		for (var i = 0; i < 5; i++) bus.Emit(new TestEvent(i));

		Assert.Equal([0, 1, 2, 3, 4], Values(first.Read()));
		Assert.True(first.Read().IsEmpty);

		// The first reader's progress does not affect the second.
		Assert.True(second.TryRead(out var e));
		Assert.Equal(0, e.Value);
		Assert.Equal([1, 2, 3, 4], Values(second.Read()));

		bus.Emit(new TestEvent(5));
		Assert.Equal([5], Values(first.Read()));
		Assert.Equal([5], Values(second.Read()));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ACopiedReaderKeepsItsOwnPosition()
	{
		var bus = new EventBus();
		var reader = bus.Reader<TestEvent>();
		bus.Emit(new TestEvent(1));

		var copy = reader;
		Assert.True(copy.TryRead(out _));

		Assert.Equal(1, reader.Count);
		Assert.Equal(0, copy.Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EventsAreVisibleInTheFrameTheyAreEmittedAndTheNext()
	{
		var bus = new EventBus();
		var early = bus.Reader<TestEvent>();
		var late = bus.Reader<TestEvent>();
		var never = bus.Reader<TestEvent>();

		bus.Emit(new TestEvent(1));
		Assert.True(early.TryRead(out _));

		bus.Step();

		// Next frame: a reader that already read it does not see it again; one that did not, does.
		Assert.False(early.TryRead(out _));
		Assert.True(late.TryRead(out _));

		bus.Step();

		// Two frames later it is gone.
		Assert.False(never.Any());
		Assert.True(never.Read().IsEmpty);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ReadReturnsThePreviousFrameFirst()
	{
		var bus = new EventBus();
		var reader = bus.Reader<TestEvent>();

		bus.Emit(new TestEvent(1));
		bus.Step();
		bus.Emit(new TestEvent(2));
		bus.Emit(new TestEvent(3));

		Assert.Equal(3, reader.Count);
		Assert.Equal([1, 2, 3], Values(reader.Read()));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TryReadLatestReturnsTheNewestUnreadEvent()
	{
		var bus = new EventBus();
		var reader = bus.Reader<TestEvent>();

		bus.Emit(new TestEvent(1));
		bus.Step();
		bus.Emit(new TestEvent(2));
		bus.Emit(new TestEvent(3));

		Assert.True(reader.TryReadLatest(out var latest));
		Assert.Equal(3, latest.Value);
		Assert.False(reader.TryReadLatest(out _));

		bus.Emit<TestEvent>();
		Assert.True(reader.TryReadLatest(out latest));
		Assert.Equal(default, latest);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SkipMarksEverythingRead()
	{
		var bus = new EventBus();
		var reader = bus.Reader<TestEvent>();
		bus.Emit(new TestEvent(1));

		reader.Skip();

		Assert.False(reader.Any());
		bus.Emit(new TestEvent(2));
		Assert.Equal([2], Values(reader.Read()));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ADefaultReaderHasNoEvents()
	{
		var reader = default(EventReader<TestEvent>);

		Assert.Null(reader.Channel);
		Assert.False(reader.Any());
		Assert.False(reader.TryRead(out _));
		Assert.True(reader.Read().IsEmpty);
		Assert.False(reader.TryReadLatest(out _));
		Assert.Equal(0, reader.Count);
		reader.Skip();
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ChannelsAreSeparatePerType()
	{
		var bus = new EventBus();
		var tests = bus.Reader<TestEvent>();
		var others = bus.Reader<OtherEvent>();

		bus.Emit(new TestEvent(1));
		bus.Emit(new OtherEvent(2));
		bus.Emit(new TestEvent(3));

		Assert.Equal([1, 3], Values(tests.Read()));
		Assert.True(others.TryRead(out var other));
		Assert.Equal(2, other.Value);

		Assert.Equal(2, bus.Channels.Count);
		Assert.Same(bus.Channel<TestEvent>(), bus.Find(typeof(TestEvent)));
		Assert.Null(bus.Find(typeof(ExitGameEvent)));
		Assert.Same(bus.Channel<TestEvent>(), tests.Channel);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AChannelGrowsPastItsCapacityAndNeverShrinks()
	{
		var bus = new EventBus();
		var grew = new List<EventChannel>();
		bus.ChannelGrew += grew.Add;
		var reader = bus.Reader<TestEvent>();
		var channel = bus.Channel<TestEvent>();
		Assert.Equal(EventBus.DefaultCapacity, channel.Capacity);

		for (var i = 0; i < 200; i++) bus.Emit(new TestEvent(i));

		Assert.Equal(200, channel.CurrentFrameCount);
		Assert.True(channel.Capacity >= 200);
		Assert.Contains(channel, grew);
		Assert.Equal(Enumerable.Range(0, 200), Values(reader.Read()));

		var capacity = channel.Capacity;
		for (var frame = 0; frame < 10; frame++)
		{
			bus.Emit(new TestEvent(frame));
			bus.Step();
		}

		Assert.Equal(capacity, channel.Capacity);
		Assert.Equal(200 + 10, channel.EmittedCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ALaggingReaderKeepsItsEventsAcrossCompaction()
	{
		// Emits more per frame than the channel holds; one reader reads every frame, the other every second frame, so
		// the window moves (and the array compacts) between the lagging reader's reads.
		var bus = new EventBus();
		var everyFrame = bus.Reader<TestEvent>();
		var everyOtherFrame = bus.Reader<TestEvent>();
		var seenEveryFrame = new List<int>();
		var seenEveryOtherFrame = new List<int>();
		var next = 0;

		for (var frame = 0; frame < 50; frame++)
		{
			for (var i = 0; i < 7 + frame % 5; i++) bus.Emit(new TestEvent(next++));

			seenEveryFrame.AddRange(Values(everyFrame.Read()));
			if (frame % 2 == 1) seenEveryOtherFrame.AddRange(Values(everyOtherFrame.Read()));
			bus.Step();
		}

		Assert.Equal(Enumerable.Range(0, next), seenEveryFrame);
		Assert.Equal(seenEveryFrame.Take(seenEveryOtherFrame.Count), seenEveryOtherFrame);
		Assert.True(seenEveryOtherFrame.Count > next - 20);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EmitAndReadDoNotAllocateInSteadyState()
	{
		var bus = new EventBus();
		var readers = new[] { bus.Reader<TestEvent>(), bus.Reader<TestEvent>() };
		var others = bus.Reader<OtherEvent>();
		var sum = 0L;

		void Frame()
		{
			for (var i = 0; i < 100; i++)
			{
				bus.Emit(new TestEvent(i));
				bus.Emit(new OtherEvent(i));
			}

			for (var r = 0; r < readers.Length; r++)
			{
				foreach (ref readonly var e in readers[r].Read()) sum += e.Value;
			}

			while (others.TryRead(out var o)) sum += o.Value;
			bus.Step();
		}

		for (var i = 0; i < 10; i++) Frame();

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 100; i++) Frame();
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.True(sum > 0);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EventTypesGetStableIds()
	{
		var id = EventId<TestEvent>.Value;

		Assert.Equal(id, EventId<TestEvent>.Value);
		Assert.NotEqual(id, EventId<OtherEvent>.Value);
		Assert.True(id >= EventIds.FirstRuntimeId, "Types the generator did not see get runtime ids.");
		Assert.False(EventId<TestEvent>.IsGenerated);
		Assert.Equal(typeof(TestEvent), EventIds.TypeOf(id));
		Assert.Equal("TestEvent", EventId<TestEvent>.Name);
		Assert.Equal(id, new EventBus().Channel<TestEvent>().Id);

		// A compile-time id cannot be assigned once the type has one, or when another type has it.
		Assert.False(EventId<TestEvent>.TryAssign(12345));
		Assert.True(EventId<TestEvent>.TryAssign(id));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EventIdsCanBeAssignedAtCompileTime()
	{
		Assert.True(EventId<AssignedEvent>.TryAssign(4242));
		Assert.Equal(4242, EventId<AssignedEvent>.Value);
		Assert.True(EventId<AssignedEvent>.IsGenerated);
		Assert.False(EventId<OtherAssignedEvent>.TryAssign(4242));
		Assert.Equal("AssignedEvent", EventIds.TypeOf(4242)!.Name);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AReaderSetKeepsOneCursorPerType()
	{
		var bus = new EventBus();
		var set = new EventReaderSet(bus);

		bus.Emit(new TestEvent(1));
		bus.Emit(new TestEvent(2));
		bus.Emit(new OtherEvent(3));

		Assert.True(set.Any<TestEvent>());
		Assert.True(set.TryRead<TestEvent>(out var first));
		Assert.Equal(1, first.Value);
		Assert.True(set.TryReadLatest<TestEvent>(out var latest));
		Assert.Equal(2, latest.Value);
		Assert.False(set.TryRead<TestEvent>(out _));
		Assert.True(set.TryRead<OtherEvent>(out _));
		Assert.Same(bus, set.Events);
	}

	private record struct AssignedEvent;
	private record struct OtherAssignedEvent;

	private static List<int> Values(ReadOnlySpan<TestEvent> events)
	{
		var values = new List<int>(events.Length);
		foreach (var e in events) values.Add(e.Value);
		return values;
	}
}
