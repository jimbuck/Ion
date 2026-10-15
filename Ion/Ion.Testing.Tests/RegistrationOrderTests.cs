using Ion.Extensions.Graphics;
using Ion.Testing;

namespace Ion.Tests;

/// <summary>
/// A game system registered before <c>UseIon()</c>: it must still see the frame's input (applied by the engine's input
/// step) and draw inside the sprite batch scope.
/// </summary>
public sealed class EarlyRegisteredSystem(IInputState input, ISpriteBatch spriteBatch)
{
	public int ClicksSeen { get; private set; }

	[First]
	public void ReadInput(GameTime dt)
	{
		if (input.Pressed(MouseButton.Left)) ClicksSeen++;
	}

	[Render]
	public void Draw(GameTime dt) => spriteBatch.DrawRect(Color.Red, new RectangleF(0, 0, 10, 10));
}

public class RegistrationOrderTests
{
	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ASystemRegisteredBeforeUseIonRunsBetweenEngineSetupAndTeardown()
	{
		using var host = new IonTestHost().UseGame(
			builder => builder.Services.AddIon(builder.Configuration).AddSingleton<EarlyRegisteredSystem>(),
			app => app.UseSystem<EarlyRegisteredSystem>().UseIon());

		var system = host.Get<EarlyRegisteredSystem>();
		host.Input.Click(MouseButton.Left);
		host.Step();

		// The input step (order StageOrder.Input) ran before the system's First step (order 0).
		Assert.Equal(1, system.ClicksSeen);

		// The rectangle was drawn between the sprite batch scope's Begin and End.
		Assert.Equal(1, host.SpriteBatch.LastFrame.Rects);

		var schedule = host.Application.PrintSchedule();
		var begin = schedule.IndexOf("NullSpriteBatchSystem.Begin {", StringComparison.Ordinal);
		var draw = schedule.IndexOf("EarlyRegisteredSystem.Draw", StringComparison.Ordinal);
		var end = schedule.IndexOf("} NullSpriteBatchSystem.End", StringComparison.Ordinal);
		Assert.True(begin >= 0 && begin < draw && draw < end, schedule);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void FunctionStepsAddedWithConfigureAppRunAtTheirOrder()
	{
		var calls = new List<string>();
		using var host = new IonTestHost()
			.ConfigureApp(app => app
				.Update(dt => calls.Add("late"), order: 10)
				.Update((GameTime dt, IInputState input) => calls.Add("early"), order: -10));

		host.Step();

		Assert.Equal(["early", "late"], calls);
	}
}
