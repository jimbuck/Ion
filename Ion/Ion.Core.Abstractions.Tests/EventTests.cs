namespace Ion.Core.Tests;

public record struct DatalessEvent();
public record struct DatafullEvent(int A, int B);

public class EventTests
{
    [Fact, Trait(CATEGORY, UNIT)]
    public void EventEmitter_Emit_Dataless()
    {
        var eventEmitter = new EventEmitter();

        Assert.Empty(eventEmitter.GetEvents<DatalessEvent>());
        Assert.Empty(eventEmitter.GetEvents<DatafullEvent>());

        eventEmitter.Emit<DatalessEvent>();

        Assert.Single(eventEmitter.GetEvents<DatalessEvent>());
        Assert.Empty(eventEmitter.GetEvents<DatafullEvent>());
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventEmitter_Emit_Data()
    {
        var eventEmitter = new EventEmitter();

        Assert.Empty(eventEmitter.GetEvents<DatalessEvent>());
        Assert.Empty(eventEmitter.GetEvents<DatafullEvent>());

        var resizeEvent = new DatafullEvent(60, 14);
        eventEmitter.Emit(resizeEvent);

        var resizeEvents = eventEmitter.GetEvents<DatafullEvent>().ToArray();

        Assert.Single(resizeEvents);
        Assert.Empty(eventEmitter.GetEvents<DatalessEvent>());

        Assert.Equal(resizeEvent, resizeEvents[0].Data);
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventEmitter_EventsLastTwoFrames()
    {
		var eventEmitter = new EventEmitter();

		eventEmitter.Emit(new DatafullEvent(60, 14));

        Assert.Single(eventEmitter.GetEvents<DatafullEvent>());
		eventEmitter.Step();
		eventEmitter.Emit(new DatafullEvent(23, 19));
        Assert.Equal(2, eventEmitter.GetEvents<DatafullEvent>().Count());
		eventEmitter.Step();
		Assert.Single(eventEmitter.GetEvents<DatafullEvent>());
		eventEmitter.Step();
		Assert.Empty(eventEmitter.GetEvents<DatafullEvent>());
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_SingleEvent()
    {
        var eventEmitter = new EventEmitter();
        var eventListener = new EventListener(eventEmitter);

        Assert.Empty(eventEmitter.GetEvents<DatafullEvent>());

        var resizeEvent = new DatafullEvent(60, 14);
        eventEmitter.Emit(resizeEvent);

        Assert.Single(eventEmitter.GetEvents<DatafullEvent>());

        Assert.True(eventListener.On<DatafullEvent>(out var e));
        Assert.Equal(resizeEvent, e?.Data);
        Assert.False(eventListener.On<DatafullEvent>(out _));
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_DoubleEvent()
    {
		var eventEmitter = new EventEmitter();
		var eventListener = new EventListener(eventEmitter);

		Assert.Empty(eventEmitter.GetEvents<DatafullEvent>());

        var resizeEvent1 = new DatafullEvent(60, 14);
		eventEmitter.Emit(resizeEvent1);

        Assert.Single(eventEmitter.GetEvents<DatafullEvent>());

        var resizeEvent2 = new DatafullEvent(23, 19);
		eventEmitter.Emit(resizeEvent2);

        Assert.Equal(2, eventEmitter.GetEvents<DatafullEvent>().Count());

        Assert.True(eventListener.On<DatafullEvent>(out var e));

        Assert.Equal(resizeEvent1, e?.Data);

		eventEmitter.Step();

		Assert.True(eventListener.On<DatafullEvent>(out e));

        Assert.Equal(resizeEvent2, e?.Data);

		eventEmitter.Step();

		Assert.False(eventListener.On<DatafullEvent>());
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_OnLatest()
    {
        var eventEmitter = new EventEmitter();
        var eventListener = new EventListener(eventEmitter);

		Assert.Empty(eventEmitter.GetEvents<DatafullEvent>());

        var resizeEvent1 = new DatafullEvent(60, 14);
        eventEmitter.Emit(resizeEvent1);

        Assert.Single(eventEmitter.GetEvents<DatafullEvent>());

        var resizeEvent2 = new DatafullEvent(23, 19);
        eventEmitter.Emit(resizeEvent2);

        Assert.Equal(2, eventEmitter.GetEvents<DatafullEvent>().Count());

        Assert.True(eventListener.OnLatest<DatafullEvent>(out var e));
        Assert.Equal(resizeEvent2, e?.Data);

		Assert.False(eventListener.On<DatafullEvent>(out _));
    }

	[Fact, Trait(CATEGORY, UNIT)]
	public void EventListener_Separate()
	{
		var eventEmitter = new EventEmitter();
		var eventListener1 = new EventListener(eventEmitter);
		var eventListener2 = new EventListener(eventEmitter);

		Assert.Empty(eventEmitter.GetEvents<DatafullEvent>());

		var resizeEvent1 = new DatafullEvent(60, 14);
		eventEmitter.Emit(resizeEvent1);

		Assert.Single(eventEmitter.GetEvents<DatafullEvent>());

		var resizeEvent2 = new DatafullEvent(23, 19);
		eventEmitter.Emit(resizeEvent2);

		Assert.Equal(2, eventEmitter.GetEvents<DatafullEvent>().Count());

		Assert.True(eventListener1.OnLatest<DatafullEvent>(out var e1));
		Assert.Equal(resizeEvent2, e1?.Data);

		Assert.True(eventListener2.OnLatest<DatafullEvent>(out var e2));
		Assert.Equal(resizeEvent2, e2?.Data);

		Assert.False(eventListener1.On<DatafullEvent>(out _));
		Assert.False(eventListener2.On<DatafullEvent>(out _));
	}

	[Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_Handled()
    {
        var eventEmitter = new EventEmitter();
        var eventListener1 = new EventListener(eventEmitter);
		var eventListener2 = new EventListener(eventEmitter);

		eventEmitter.Emit<DatalessEvent>();

        Assert.True(eventListener1.On<DatalessEvent>());
        Assert.True(eventListener2.On<DatalessEvent>());

        eventEmitter.Emit(new DatafullEvent(60, 14));

		Assert.True(eventListener1.On<DatafullEvent>(out var e));
		Assert.NotNull(e);
		e!.Handled = true;
		Assert.False(eventListener2.On<DatafullEvent>(out _));
	}

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_Throughput()
    {
		var eventEmitter = new EventEmitter();
		var eventListener1 = new EventListener(eventEmitter);
		var eventListener2 = new EventListener(eventEmitter);

		const int ITEM_COUNT = 100_000;
        var resize1 = 0;
        var resize2 = 0;
        var close1 = 0;
        var close2 = 0;

        for (var i = 0; i < ITEM_COUNT; i++)
        {
			eventEmitter.Step();

			eventEmitter.Emit<DatalessEvent>();
			eventEmitter.Emit(new DatafullEvent(i, i));

            if(eventListener1.On<DatalessEvent>()) close1++;
            if(eventListener1.On<DatafullEvent>(out var e))
            {
                resize1++;
                e.Handled = true;
            }

            if (eventListener2.On<DatalessEvent>()) close2++;
            if (eventListener2.On<DatafullEvent>()) resize2++;
        }
        
        Assert.Equal(ITEM_COUNT, close1);
        Assert.Equal(ITEM_COUNT, close2);
        Assert.Equal(ITEM_COUNT, resize1);
        Assert.Equal(0, resize2);
    }
}
