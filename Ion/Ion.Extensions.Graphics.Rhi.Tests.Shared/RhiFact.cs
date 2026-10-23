using System.ComponentModel;

using Xunit.Abstractions;
using Xunit.Sdk;

namespace Ion.Extensions.Graphics.Rhi.Tests;

/// <summary>
/// Names the backend a concrete contract test class runs on. Put it on the sealed class a backend's test project derives
/// from a shared contract (for example <c>[RhiBackend(GraphicsBackend.OpenGLES)] public sealed class GlesHeadlessContractTests : HeadlessContractTests;</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RhiBackendAttribute(GraphicsBackend backend) : Attribute
{
	/// <summary>The backend.</summary>
	public GraphicsBackend Backend { get; } = backend;
}

/// <summary>
/// A contract test: runs on the backend named by the test class's <see cref="RhiBackendAttribute"/>, and is skipped when
/// that backend (or, for <see cref="Windowed"/> tests, a display) is missing on this machine.
/// </summary>
[XunitTestCaseDiscoverer("Ion.Extensions.Graphics.Rhi.Tests.RhiFactDiscoverer", "Ion.Extensions.Graphics.Rhi.Tests.Shared")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RhiFactAttribute : FactAttribute
{
	/// <summary>True for tests that open a window.</summary>
	public bool Windowed { get; set; }
}

/// <summary>Discovers <see cref="RhiFactAttribute"/> tests as <see cref="RhiTestCase"/>s, which compute their skip reason from the concrete class.</summary>
public sealed class RhiFactDiscoverer(IMessageSink diagnosticMessageSink) : IXunitTestCaseDiscoverer
{
	/// <inheritdoc/>
	public IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
	{
		yield return new RhiTestCase(diagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
	}
}

/// <summary>A contract test case: skipped when the test class's backend is not available.</summary>
public sealed class RhiTestCase : XunitTestCase
{
	/// <summary>For deserialization.</summary>
	[EditorBrowsable(EditorBrowsableState.Never)]
	[Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
	public RhiTestCase() { }

	/// <summary>Creates the test case.</summary>
	public RhiTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod)
		: base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod) { }

	/// <inheritdoc/>
	protected override string? GetSkipReason(IAttributeInfo factAttribute)
	{
		var declared = base.GetSkipReason(factAttribute);
		if (!string.IsNullOrEmpty(declared)) return declared;
		var backend = TestMethod.TestClass.Class.GetCustomAttributes(typeof(RhiBackendAttribute)).FirstOrDefault()?.GetConstructorArguments().FirstOrDefault() as GraphicsBackend?;
		if (backend is null) return $"{TestMethod.TestClass.Class.Name} has no [RhiBackend] attribute.";
		var windowed = factAttribute.GetNamedArgument<bool>(nameof(RhiFactAttribute.Windowed));
		return TestEnvironment.SkipReason(backend.Value, windowed);
	}
}
