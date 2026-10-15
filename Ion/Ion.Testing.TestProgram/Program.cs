using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Testing.TestProgram;

// The program the entry point tests run. It registers a system that counts frames and emits a Tick event every frame.
//   --Test:Speed=<n>          the counter's step (read while registering, so a test host's configuration must be in place)
//   --throw-before-run        throws before Run()
//   --return-before-run       returns without calling Run()
//   --throw-after-run         throws after Run() returns
var builder = IonApplication.CreateBuilder(args);
if (args.Contains("--throw-before-run")) throw new InvalidOperationException("The program failed before Run().");

var speed = int.TryParse(builder.Configuration["Test:Speed"], out var configured) ? configured : 1;
builder.AddIon().AddSystem<Counter>();
builder.Services.AddSingleton(new CounterSettings(speed));

using var game = builder.Build();
game.UseIon().UseSystem<Counter>();
if (args.Contains("--return-before-run")) return;

game.Run();

Exits.Returned = true;
if (args.Contains("--throw-after-run")) throw new InvalidOperationException("The program failed after Run().");

namespace Ion.Testing.TestProgram
{
	/// <summary>The counter's step.</summary>
	/// <param name="Speed">Added every frame.</param>
	public sealed record CounterSettings(int Speed);

	/// <summary>An event emitted every frame.</summary>
	/// <param name="Count">The count after the frame.</param>
	public readonly record struct Tick(int Count);

	/// <summary>Counts frames (by <see cref="CounterSettings.Speed"/>) and emits <see cref="Tick"/>.</summary>
	public sealed class Counter(CounterSettings settings, IEvents events)
	{
		/// <summary>The count.</summary>
		public int Count { get; private set; }

		/// <summary>Counts.</summary>
		[Update]
		public void Update(GameTime dt)
		{
			Count += settings.Speed;
#pragma warning disable ION101 // Read by the tests (IonTestHost.Collect<Tick>()), outside this program.
			events.Emit(new Tick(Count));
#pragma warning restore ION101
		}
	}

	/// <summary>What the program did after Run() returned.</summary>
	public static class Exits
	{
		/// <summary>Whether Run() returned (on the last run).</summary>
		public static volatile bool Returned;
	}
}
