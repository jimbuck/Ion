using System.ComponentModel;

namespace Ion;

/// <summary>
/// Emitted by the Ion source generator on every assembly it compiles: what a public method taking an application or
/// scene builder registers on it (systems, function steps, middleware, calls to other such methods), with the generated
/// call sites. The generator of an application that calls the method reads it to emit the whole schedule at compile time.
/// </summary>
/// <param name="method">The documentation comment id of the method, then <c>#</c> and the index of the builder parameter.</param>
/// <param name="registrations">The registrations, one per line (the generator's format).</param>
/// <param name="types">The types the registrations refer to, by index.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ScheduleRegistrationsAttribute(string method, string registrations, params Type[] types) : Attribute
{
	/// <summary>The documentation comment id of the method, then <c>#</c> and the index of the builder parameter.</summary>
	public string Method { get; } = method;

	/// <summary>The registrations, one per line.</summary>
	public string Registrations { get; } = registrations;

	/// <summary>The types the registrations refer to, by index.</summary>
	public Type[] Types { get; } = types;
}
