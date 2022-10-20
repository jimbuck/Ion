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
        Assert.Empty(eventSystem.GetEvents<SurfaceResizeEvent>());

        eventSystem.Emit<WindowClosedEvent>();

        Assert.Single(eventSystem.GetEvents<WindowClosedEvent>());
        Assert.Empty(eventSystem.GetEvents<SurfaceResizeEvent>());
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventEmitter_Emit_Data()
    {
        var eventSystem = new EventSystem();

        Assert.Empty(eventSystem.GetEvents<WindowClosedEvent>());
        Assert.Empty(eventSystem.GetEvents<SurfaceResizeEvent>());

        var resizeEvent = new SurfaceResizeEvent(60, 14);
        eventSystem.Emit(resizeEvent);

        var resizeEvents = eventSystem.GetEvents<SurfaceResizeEvent>().ToArray();

        Assert.Single(resizeEvents);
        Assert.Empty(eventSystem.GetEvents<WindowClosedEvent>());

        Assert.Equal(resizeEvent, resizeEvents[0].Data);
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventEmitter_EventsLastTwoFrames()
    {
        var eventSystem = new EventSystem();

        eventSystem.Emit(new SurfaceResizeEvent(60, 14));

        Assert.Single(eventSystem.GetEvents<SurfaceResizeEvent>());
        eventSystem.PreUpdate(DT);
        eventSystem.Emit(new SurfaceResizeEvent(23, 19));
        Assert.Equal(2, eventSystem.GetEvents<SurfaceResizeEvent>().Count());
        eventSystem.PreUpdate(DT);
        Assert.Single(eventSystem.GetEvents<SurfaceResizeEvent>());
        eventSystem.PreUpdate(DT);
        Assert.Empty(eventSystem.GetEvents<SurfaceResizeEvent>());
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_SingleEvent()
    {
        var eventSystem = new EventSystem();
        var eventListener = eventSystem.CreateListener();

        Assert.Empty(eventSystem.GetEvents<SurfaceResizeEvent>());

        var resizeEvent = new SurfaceResizeEvent(60, 14);
        eventSystem.Emit(resizeEvent);

        Assert.Single(eventSystem.GetEvents<SurfaceResizeEvent>());

        Assert.True(eventListener.On<SurfaceResizeEvent>(out var e));
        Assert.Equal(resizeEvent, e?.Data);
        Assert.False(eventListener.On<SurfaceResizeEvent>(out _));
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_DoubleEvent()
    {
        var eventSystem = new EventSystem();
        var eventListener = eventSystem.CreateListener();

        Assert.Empty(eventSystem.GetEvents<SurfaceResizeEvent>());

        var resizeEvent1 = new SurfaceResizeEvent(60, 14);
        eventSystem.Emit(resizeEvent1);

        Assert.Single(eventSystem.GetEvents<SurfaceResizeEvent>());

        var resizeEvent2 = new SurfaceResizeEvent(23, 19);
        eventSystem.Emit(resizeEvent2);

        Assert.Equal(2, eventSystem.GetEvents<SurfaceResizeEvent>().Count());

        Assert.True(eventListener.On<SurfaceResizeEvent>(out var e));

        Assert.Equal(resizeEvent1, e?.Data);

        eventSystem.PreUpdate(DT);

        Assert.True(eventListener.On<SurfaceResizeEvent>(out e));

        Assert.Equal(resizeEvent2, e?.Data);

        eventSystem.PreUpdate(DT);

        Assert.False(eventListener.On<SurfaceResizeEvent>(out _));
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void EventListener_OnLatest()
    {
        var eventSystem = new EventSystem();
        var eventListener = eventSystem.CreateListener();

        Assert.Empty(eventSystem.GetEvents<SurfaceResizeEvent>());

        var resizeEvent1 = new SurfaceResizeEvent(60, 14);
        eventSystem.Emit(resizeEvent1);

        Assert.Single(eventSystem.GetEvents<SurfaceResizeEvent>());

        var resizeEvent2 = new SurfaceResizeEvent(23, 19);
        eventSystem.Emit(resizeEvent2);

        Assert.Equal(2, eventSystem.GetEvents<SurfaceResizeEvent>().Count());

        Assert.True(eventListener.OnLatest<SurfaceResizeEvent>(out var e));

        Assert.Equal(resizeEvent2, e?.Data);

        Assert.False(eventListener.On<SurfaceResizeEvent>(out _));
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

        eventSystem.Emit(new SurfaceResizeEvent(60, 14));

        Assert.True(eventListener1.On<SurfaceResizeEvent>(out var e));
        e.Handled = true;
        Assert.False(eventListener2.On<SurfaceResizeEvent>(out _));
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
            eventSystem.Emit(new SurfaceResizeEvent((uint)i, (uint)i));

            if(eventListener1.On<WindowClosedEvent>()) close1++;
            if(eventListener1.On<SurfaceResizeEvent>(out var e))
            {
                resize1++;
                e.Handled = true;
            }

            if (eventListener2.On<WindowClosedEvent>()) close2++;
            if (eventListener2.On<SurfaceResizeEvent>()) resize2++;
        }
        
        Assert.Equal(ITEM_COUNT, close1);
        Assert.Equal(ITEM_COUNT, close2);
        Assert.Equal(ITEM_COUNT, resize1);
        Assert.Equal(0, resize2);
    }
}
