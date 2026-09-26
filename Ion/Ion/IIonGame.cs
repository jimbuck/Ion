namespace Ion;

/// <summary>
/// A game's setup as two static methods, so tests and tools can build the same game the program runs:
/// <c>IonTestHost.Run&lt;TGame&gt;(frames)</c> in <c>Ion.Testing</c> builds it headless. The templates' <c>Program.cs</c>
/// calls them directly (<c>Game.Configure(builder)</c>, <c>Game.Use(app)</c>) so the schedule generator sees the
/// registrations.
/// </summary>
/// <example>
/// <code>
/// public sealed class Game : IIonGame
/// {
///     public static void Configure(IonApplicationBuilder builder)
///     {
///         builder.Services.AddIon(builder.Configuration);
///         builder.Services.AddSingleton&lt;PlayerSystem&gt;();
///     }
///
///     public static void Use(IIonApplication app) =&gt; app.UseIon().UseSystem&lt;PlayerSystem&gt;();
/// }
/// </code>
/// </example>
public interface IIonGame
{
	/// <summary>Registers the engine (<c>AddIon</c>) and the game's services and systems.</summary>
	static abstract void Configure(IonApplicationBuilder builder);

	/// <summary>Adds the engine's systems (<c>UseIon</c>) and the game's systems to the schedule.</summary>
	static abstract void Use(IIonApplication app);
}
