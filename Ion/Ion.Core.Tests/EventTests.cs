
namespace Ion.Tests;

public class EventTests
{
	public record struct TestEvent();

	[Fact, Trait(CATEGORY, UNIT)]
	public void BasicEventTest()
	{
		var emitter = new EventEmitter();
		var listener1 = new EventListener(emitter);

		emitter.Emit(new TestEvent());

		// Hears the event.
		Assert.True(listener1.On<TestEvent>(out var e1));
		Assert.NotNull(e1);

		// Does not hear again.
		Assert.False(listener1.On<TestEvent>());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void MultiListenerTest()
	{
		var emitter = new EventEmitter();
		var listener1 = new EventListener(emitter);
		var listener2 = new EventListener(emitter);

		emitter.Emit(new TestEvent());

		// Hears the event.
		Assert.True(listener1.On<TestEvent>(out var e1));
		Assert.NotNull(e1);

		// Does not hear again.
		Assert.False(listener1.On<TestEvent>());

		// Hears the event.
		Assert.True(listener2.On<TestEvent>(out var e2));
		Assert.NotNull(e2);

		// Does not hear again.
		Assert.False(listener2.On<TestEvent>());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EventEmitterStepTest()
	{
		var emitter = new EventEmitter();
		var listener1 = new EventListener(emitter);
		var listener2 = new EventListener(emitter);

		emitter.Emit(new TestEvent());

		// Hears the event.
		Assert.True(listener1.On<TestEvent>(out var e1));
		Assert.NotNull(e1);

		// Does not hear again.
		Assert.False(listener1.On<TestEvent>());

		// Step the emitter.
		emitter.Step();

		// Does not hear again.
		Assert.False(listener1.On<TestEvent>());

		Assert.True(listener2.On<TestEvent>(out var e2));
		Assert.NotNull(e2);

		emitter.Step();

		Assert.False(listener2.On<TestEvent>());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EventHandledTest()
	{
		var emitter = new EventEmitter();
		var listener1 = new EventListener(emitter);
		var listener2 = new EventListener(emitter);

		emitter.Emit(new TestEvent());

		// Hears the event.
		Assert.True(listener1.On<TestEvent>(out var e1));
		Assert.NotNull(e1);
		e1.Handled = true;

		// Does not hear again.
		Assert.False(listener2.On<TestEvent>());
	}
}
