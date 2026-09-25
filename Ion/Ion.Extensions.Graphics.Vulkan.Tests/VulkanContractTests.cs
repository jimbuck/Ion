global using System.Numerics;

global using Microsoft.Extensions.DependencyInjection;

global using Xunit;

global using static Ion.Tests.TestConstants;

using Ion.Extensions.Graphics.Rhi.Tests;

namespace Ion.Extensions.Graphics.Vulkan.Tests;

/// <summary>The shared headless RHI contract (Ion.Extensions.Graphics.Rhi.Tests.Shared) on Vulkan.</summary>
[RhiBackend(GraphicsBackend.Vulkan)]
public sealed class VulkanHeadlessContractTests : HeadlessContractTests;

/// <summary>The shared windowed RHI contract on Vulkan (a swapchain on a GLFW or SDL window).</summary>
[RhiBackend(GraphicsBackend.Vulkan)]
[Collection(WindowedContractTests.Collection)]
public sealed class VulkanWindowedContractTests : WindowedContractTests;

/// <summary>Windowed tests share the process-wide Silk.NET platform registration, so they never run in parallel.</summary>
[CollectionDefinition(WindowedContractTests.Collection, DisableParallelization = true)]
public sealed class WindowedCollection;

/// <summary>A test that needs a Vulkan driver; skipped when there is none.</summary>
public sealed class VulkanFactAttribute : FactAttribute
{
	/// <summary>Skips the test when no Vulkan driver is installed.</summary>
	public VulkanFactAttribute()
	{
		if (!TestEnvironment.HasVulkan) Skip = "No Vulkan driver (on Linux install Mesa lavapipe: mesa-vulkan-drivers).";
	}
}
