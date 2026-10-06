global using System.Numerics;

global using Microsoft.Extensions.DependencyInjection;

global using Xunit;

global using static Ion.Tests.TestConstants;

using Ion.Extensions.Graphics.Rhi;
using Ion.Extensions.Graphics.Rhi.Tests;
using Ion.Testing;

namespace Ion.Extensions.Graphics.GLES.Tests;

/// <summary>The shared headless RHI contract (Ion.Extensions.Graphics.Rhi.Tests.Shared) on OpenGL ES, at the driver's feature level (ES 3.2 on llvmpipe).</summary>
[RhiBackend(GraphicsBackend.OpenGLES)]
public sealed class GlesHeadlessContractTests : HeadlessContractTests;

/// <summary>
/// The same contract on the OpenGL ES 3.1 paths (the R36S's Mali-G31 level): base vertex emulated through vertex buffer
/// offsets.
/// </summary>
[RhiBackend(GraphicsBackend.OpenGLES)]
public sealed class GlesEs31HeadlessContractTests : HeadlessContractTests
{
	protected override IGraphicsDevice CreateDevice(ValidationLog log, int framesInFlight = 2) => log.CreateDevice(Backend, framesInFlight, GlesFeatureLevel.Es31);

	protected override IonTestHost ConfigureHost(IonTestHost host) => base.ConfigureHost(host).WithConfiguration("Ion:Graphics:Gles:MaxFeatureLevel", nameof(GlesFeatureLevel.Es31));

	protected override void ConfigureSample(IDictionary<string, string?> settings) => settings["Ion:Graphics:Gles:MaxFeatureLevel"] = nameof(GlesFeatureLevel.Es31);
}

/// <summary>
/// The same contract on the OpenGL ES 3.0 fallback: GLSL ES 3.00 rewritten at load with slots assigned by name, vertex
/// attribute pointers per draw, base vertex emulated.
/// </summary>
[RhiBackend(GraphicsBackend.OpenGLES)]
public sealed class GlesEs30HeadlessContractTests : HeadlessContractTests
{
	protected override IGraphicsDevice CreateDevice(ValidationLog log, int framesInFlight = 2) => log.CreateDevice(Backend, framesInFlight, GlesFeatureLevel.Es30);

	protected override IonTestHost ConfigureHost(IonTestHost host) => base.ConfigureHost(host).WithConfiguration("Ion:Graphics:Gles:MaxFeatureLevel", nameof(GlesFeatureLevel.Es30));

	protected override void ConfigureSample(IDictionary<string, string?> settings) => settings["Ion:Graphics:Gles:MaxFeatureLevel"] = nameof(GlesFeatureLevel.Es30);
}

/// <summary>The shared texture contract (cube maps, sampled depth) on OpenGL ES at the driver's feature level.</summary>
[RhiBackend(GraphicsBackend.OpenGLES)]
public sealed class GlesTextureContractTests : TextureContractTests;

/// <summary>The shared texture contract on the OpenGL ES 3.0 fallback (GLSL ES 3.00, slots assigned by name).</summary>
[RhiBackend(GraphicsBackend.OpenGLES)]
public sealed class GlesEs30TextureContractTests : TextureContractTests
{
	protected override IGraphicsDevice CreateDevice(ValidationLog log) => log.CreateDevice(Backend, 2, GlesFeatureLevel.Es30);
}

/// <summary>The shared compute contract on OpenGL ES at the driver's feature level.</summary>
[RhiBackend(GraphicsBackend.OpenGLES)]
[Collection(ComputeCollection.Name)]
public sealed class GlesComputeContractTests : ComputeContractTests;

/// <summary>The shared compute contract on the OpenGL ES 3.1 paths (the R36S's Mali-G31 level, the lowest with compute).</summary>
[RhiBackend(GraphicsBackend.OpenGLES)]
[Collection(ComputeCollection.Name)]
public sealed class GlesEs31ComputeContractTests : ComputeContractTests
{
	protected override IGraphicsDevice CreateDevice(ValidationLog log) => log.CreateDevice(Backend, 2, GlesFeatureLevel.Es31);
}

/// <summary>The shared windowed RHI contract on OpenGL ES (the window's GL ES context, an offscreen target blitted at present).</summary>

[RhiBackend(GraphicsBackend.OpenGLES)]
[Collection(WindowedContractTests.Collection)]
public sealed class GlesWindowedContractTests : WindowedContractTests;

/// <summary>
/// Compute tests on OpenGL ES never run in parallel with other tests: ANGLE's D3D11 backend (the libEGL.dll that
/// Electron apps put on PATH on Windows) crashes or hangs in glDispatchCompute when another thread drives a second
/// context at the same time. One context per process, as in a game, is unaffected.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ComputeCollection
{
	public const string Name = "GLES compute";
}

/// <summary>Windowed tests share the process-wide Silk.NET platform registration, so they never run in parallel.</summary>

[CollectionDefinition(WindowedContractTests.Collection, DisableParallelization = true)]
public sealed class WindowedCollection;

/// <summary>A test that needs a headless OpenGL ES context (EGL); skipped when there is none.</summary>
public sealed class GlesFactAttribute : FactAttribute
{
	/// <summary>Skips the test when EGL cannot create an OpenGL ES 3 context.</summary>
	public GlesFactAttribute()
	{
		if (TestEnvironment.SkipReason(GraphicsBackend.OpenGLES, windowed: false) is { } reason) Skip = reason;
	}
}
