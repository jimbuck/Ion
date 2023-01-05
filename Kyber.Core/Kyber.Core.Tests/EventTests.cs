﻿namespace Kyber.Core.Tests;

public record struct DatalessEvent();
public record struct DatafullEvent(int A, int B);

public class EventTests
{
    [Fact, Trait(CATEGORY, UNIT)]
    public void EventEmitter_Emit_Dataless()
    {
        var eventSystem = new EventEmitter();

        Assert.Empty(eventSystem.GetEvents<DatalessEvent>());
        Assert.Empty(eventSystem.GetEvents<DatafullEvent>());

        eventSystem.Emit<DatalessEvent>();

        Assert.Single(eventSystem.GetEvents<DatalessEvent>());
        Assert.Empty(eventSystem.GetEvents<DatafullEvent>());
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventEmitter_Emit_Data()
    {
        var eventSystem = new EventEmitter();

        Assert.Empty(eventSystem.GetEvents<DatalessEvent>());
        Assert.Empty(eventSystem.GetEvents<DatafullEvent>());

        var resizeEvent = new DatafullEvent(60, 14);
        eventSystem.Emit(resizeEvent);

        var resizeEvents = eventSystem.GetEvents<DatafullEvent>().ToArray();

        Assert.Single(resizeEvents);
        Assert.Empty(eventSystem.GetEvents<DatalessEvent>());

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
        var eventSystem = new EventEmitter();
        var eventListener = eventSystem.CreateListener();

        Assert.Empty(eventSystem.GetEvents<DatafullEvent>());

        var resizeEvent = new DatafullEvent(60, 14);
        eventSystem.Emit(resizeEvent);

        Assert.Single(eventSystem.GetEvents<DatafullEvent>());

        Assert.True(eventListener.On<DatafullEvent>(out var e));
        Assert.Equal(resizeEvent, e?.Data);
        Assert.False(eventListener.On<DatafullEvent>(out _));
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_DoubleEvent()
    {
		var eventEmitter = new EventEmitter();
		var eventListener = eventEmitter.CreateListener();

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
        var eventSystem = new EventEmitter();
        var eventListener = eventSystem.CreateListener();

        Assert.Empty(eventSystem.GetEvents<DatafullEvent>());

        var resizeEvent1 = new DatafullEvent(60, 14);
        eventSystem.Emit(resizeEvent1);

        Assert.Single(eventSystem.GetEvents<DatafullEvent>());

        var resizeEvent2 = new DatafullEvent(23, 19);
        eventSystem.Emit(resizeEvent2);

        Assert.Equal(2, eventSystem.GetEvents<DatafullEvent>().Count());

        Assert.True(eventListener.OnLatest<DatafullEvent>(out var e));

        Assert.Equal(resizeEvent2, e?.Data);

        Assert.False(eventListener.On<DatafullEvent>(out _));
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_Handled()
    {
        var eventSystem = new EventEmitter();
        var eventListener1 = eventSystem.CreateListener();
        var eventListener2 = eventSystem.CreateListener();

        eventSystem.Emit<DatalessEvent>();

        Assert.True(eventListener1.On<DatalessEvent>());
        Assert.True(eventListener2.On<DatalessEvent>());

        eventSystem.Emit(new DatafullEvent(60, 14));

		Assert.True(eventListener1.On<DatafullEvent>(out var e));
		Assert.NotNull(e);
		e!.Handled = true;
		Assert.False(eventListener2.On<DatafullEvent>(out _));
	}

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_Throughput()
    {
		var eventEmitter = new EventEmitter();
		var eventListener1 = eventEmitter.CreateListener();
        var eventListener2 = eventEmitter.CreateListener();

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
