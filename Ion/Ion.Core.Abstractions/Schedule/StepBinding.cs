using System.ComponentModel;
using System.Reflection;

namespace Ion;

/// <summary>
/// Base class of attributes that bind a step method themselves instead of the schedule's own binding: a method with a
/// stage attribute and an attribute derived from this class is a step whatever its accessibility and parameters, and the
/// attribute validates it, names the services it injects and creates its <see cref="GameLoopDelegate"/>.
/// </summary>
/// <remarks>
/// This is the runtime (reflection) path of extensions such as the ECS module's <c>[Query]</c>: when a source generator
/// expands the method (see <see cref="ExpandedStepAttribute"/>), the generated method runs instead and this binder is not
/// used. Binder steps are leaf steps; they cannot be <see cref="BeginAttribute"/>/<see cref="EndAttribute"/> scopes.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public abstract class StepBinderAttribute : Attribute
{
	/// <summary>
	/// Why <paramref name="method"/> cannot be bound (reported as ION007, "unsupported signature: {reason}"), or null
	/// when it can.
	/// </summary>
	public abstract string? Validate(MethodInfo method);

	/// <summary>
	/// The services the bound step resolves from the schedule's provider, for the schedule's checks (ION006, ION008).
	/// </summary>
	public abstract IReadOnlyList<Type> GetServices(MethodInfo method);

	/// <summary>
	/// Binds <paramref name="method"/> to <paramref name="instance"/> (null for a static method), resolving its services
	/// from <paramref name="services"/>. <paramref name="name"/> is the step's printed name (<c>System.Method</c>), for
	/// error messages.
	/// </summary>
	public abstract GameLoopDelegate Bind(object? instance, MethodInfo method, IServiceProvider services, string name);
}

/// <summary>
/// Marks a method emitted by a source generator that runs in place of the method <see cref="Method"/> of the same type
/// (for example a <c>[Query]</c> method expanded into a chunk loop). The generated method carries the original's stage and
/// ordering attributes; the schedule prints and orders it under the original's name and ignores the original.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ExpandedStepAttribute(string method) : Attribute
{
	/// <summary>The name of the method this one replaces.</summary>
	public string Method { get; } = method;
}
