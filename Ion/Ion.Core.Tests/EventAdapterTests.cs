namespace Ion.Tests;

#pragma warning disable CS0618 // The obsolete adapters are what these tests cover.

/// <summary>The obsolete <see cref="IEventEmitter"/>/<see cref="IEventListener"/> adapters over <see cref="IEvents"/>.</summary>
public class EventAdapterTests
{
	public record struct PingEvent(int Value);

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheAdaptersAreRegisteredOverTheBus()
	{
		using var app = IonApplication.CreateBuilder().Build();
		var bus = app.Services.GetRequiredService<EventBus>();

		Assert.Same(app.Services.GetRequiredService<EventEmitter>(), app.Services.GetRequiredService<IEventEmitter>());
		Assert.Same(bus, app.Services.GetRequiredService<EventEmitter>().Events);
		Assert.IsType<EventListener>(app.Services.GetRequiredService<IEventListener>());
		Assert.IsType<EventListener>(app.Services.GetRequiredService<IEventListenerFactory>().CreateListener());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AListenerSeesEachEventOnceAndReadsTheBusReadersSee()
	{
		var bus = new EventBus();
		var emitter = new EventEmitter(bus);
		using var listener = new EventListener(bus);
		var reader = bus.Reader<PingEvent>();

		emitter.Emit(new PingEvent(1));
		listener.Emit<PingEvent>();

		Assert.True(listener.On<PingEvent>(out var first));
		Assert.Equal(1, first.Value);
		Assert.True(listener.On<PingEvent>());
		Assert.False(listener.On<PingEvent>());
		Assert.Equal(2, reader.Read().Length);

		emitter.Step();
		emitter.Emit(new PingEvent(3));
		emitter.Emit(new PingEvent(4));
		Assert.True(listener.OnLatest<PingEvent>(out var latest));
		Assert.Equal(4, latest.Value);
		Assert.False(listener.OnLatest<PingEvent>());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AnEmitterWithoutABusCreatesOne()
	{
		var emitter = new EventEmitter();
		var reader = emitter.Events.Reader<PingEvent>();

		emitter.Emit(new PingEvent(1));
		emitter.Step();
		emitter.Step();

		Assert.False(reader.Any());
		using var listener = new EventListener(emitter);
		Assert.False(listener.On<PingEvent>());
	}
}
