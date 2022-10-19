using Kyber.Events;

namespace Kyber.Tests;

public class EventTests
{
    [Fact, Trait(CATEGORY, UNIT)]
    public void EventEmitter_EnabledByDefault()
    {
        var eventSystem = new EventSystem();
        Assert.True(eventSystem.IsEnabled);
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventEmitter_Emit_Dataless()
    {
        var eventSystem = new EventSystem();

        Assert.Empty(eventSystem.GetEvents<WindowClosedEvent>());
        Assert.Empty(eventSystem.GetEvents<WindowResizeEvent>());

        eventSystem.Emit<WindowClosedEvent>();

        Assert.Single(eventSystem.GetEvents<WindowClosedEvent>());
        Assert.Empty(eventSystem.GetEvents<WindowResizeEvent>());
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventEmitter_Emit_Data()
    {
        var eventSystem = new EventSystem();

        Assert.Empty(eventSystem.GetEvents<WindowClosedEvent>());
        Assert.Empty(eventSystem.GetEvents<WindowResizeEvent>());

        var resizeEvent = new WindowResizeEvent(60, 14);
        eventSystem.Emit(resizeEvent);

        var resizeEvents = eventSystem.GetEvents<WindowResizeEvent>().ToArray();

        Assert.Single(resizeEvents);
        Assert.Empty(eventSystem.GetEvents<WindowClosedEvent>());

        Assert.Equal(resizeEvent, resizeEvents[0].Data);
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventEmitter_EventsLastTwoFrames()
    {
        var eventSystem = new EventSystem();

        eventSystem.Emit(new WindowResizeEvent(60, 14));

        Assert.Single(eventSystem.GetEvents<WindowResizeEvent>());
        eventSystem.PreUpdate(DT);
        eventSystem.Emit(new WindowResizeEvent(23, 19));
        Assert.Equal(2, eventSystem.GetEvents<WindowResizeEvent>().Count());
        eventSystem.PreUpdate(DT);
        Assert.Single(eventSystem.GetEvents<WindowResizeEvent>());
        eventSystem.PreUpdate(DT);
        Assert.Empty(eventSystem.GetEvents<WindowResizeEvent>());
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_SingleEvent()
    {
        var eventSystem = new EventSystem();
        var eventListener = eventSystem.CreateListener();

        Assert.Empty(eventSystem.GetEvents<WindowResizeEvent>());

        var resizeEvent = new WindowResizeEvent(60, 14);
        eventSystem.Emit(resizeEvent);

        Assert.Single(eventSystem.GetEvents<WindowResizeEvent>());

        Assert.True(eventListener.On<WindowResizeEvent>(out var e));
        Assert.Equal(resizeEvent, e?.Data);
        Assert.False(eventListener.On<WindowResizeEvent>(out _));
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_DoubleEvent()
    {
        var eventSystem = new EventSystem();
        var eventListener = eventSystem.CreateListener();

        Assert.Empty(eventSystem.GetEvents<WindowResizeEvent>());

        var resizeEvent1 = new WindowResizeEvent(60, 14);
        eventSystem.Emit(resizeEvent1);

        Assert.Single(eventSystem.GetEvents<WindowResizeEvent>());

        var resizeEvent2 = new WindowResizeEvent(23, 19);
        eventSystem.Emit(resizeEvent2);

        Assert.Equal(2, eventSystem.GetEvents<WindowResizeEvent>().Count());

        Assert.True(eventListener.On<WindowResizeEvent>(out var e));

        Assert.Equal(resizeEvent1, e?.Data);

        eventSystem.PreUpdate(DT);

        Assert.True(eventListener.On<WindowResizeEvent>(out e));

        Assert.Equal(resizeEvent2, e?.Data);

        eventSystem.PreUpdate(DT);

        Assert.False(eventListener.On<WindowResizeEvent>(out _));
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_OnLatest()
    {
        var eventSystem = new EventSystem();
        var eventListener = eventSystem.CreateListener();

        Assert.Empty(eventSystem.GetEvents<WindowResizeEvent>());

        var resizeEvent1 = new WindowResizeEvent(60, 14);
        eventSystem.Emit(resizeEvent1);

        Assert.Single(eventSystem.GetEvents<WindowResizeEvent>());

        var resizeEvent2 = new WindowResizeEvent(23, 19);
        eventSystem.Emit(resizeEvent2);

        Assert.Equal(2, eventSystem.GetEvents<WindowResizeEvent>().Count());

        Assert.True(eventListener.OnLatest<WindowResizeEvent>(out var e));

        Assert.Equal(resizeEvent2, e?.Data);

        Assert.False(eventListener.On<WindowResizeEvent>(out _));
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_Handled()
    {
        var eventSystem = new EventSystem();
        var eventListener1 = eventSystem.CreateListener();
        var eventListener2 = eventSystem.CreateListener();

        eventSystem.Emit<WindowClosedEvent>();

        Assert.True(eventListener1.On<WindowClosedEvent>());
        Assert.True(eventListener2.On<WindowClosedEvent>());

        eventSystem.Emit(new WindowResizeEvent(60, 14));

        Assert.True(eventListener1.On<WindowResizeEvent>(out var e));
        e.Handled = true;
        Assert.False(eventListener2.On<WindowResizeEvent>(out _));
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_Throughput()
    {
        var eventSystem = new EventSystem();
        var eventListener1 = eventSystem.CreateListener();
        var eventListener2 = eventSystem.CreateListener();

        const int ITEM_COUNT = 100_000;
        var resize1 = 0;
        var resize2 = 0;
        var close1 = 0;
        var close2 = 0;

        for (var i = 0; i < ITEM_COUNT; i++)
        {
            eventSystem.PreUpdate(DT);

            eventSystem.Emit<WindowClosedEvent>();
            eventSystem.Emit(new WindowResizeEvent((uint)i, (uint)i));

            if(eventListener1.On<WindowClosedEvent>()) close1++;
            if(eventListener1.On<WindowResizeEvent>(out var e))
            {
                resize1++;
                e.Handled = true;
            }

            if (eventListener2.On<WindowClosedEvent>()) close2++;
            if (eventListener2.On<WindowResizeEvent>()) resize2++;
        }
        
        Assert.Equal(ITEM_COUNT, close1);
        Assert.Equal(ITEM_COUNT, close2);
        Assert.Equal(ITEM_COUNT, resize1);
        Assert.Equal(0, resize2);
    }
}
