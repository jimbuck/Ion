extern alias VeldridGraphics;

using VeldridGraphics::Ion.Extensions.Graphics;

using IonBackend = Ion.Extensions.Graphics.GraphicsBackend;
using VeldridBackend = Veldrid.GraphicsBackend;

namespace Ion.Extensions.Graphics.Tests;

public class VeldridBackendMapperTests
{
	private static readonly IonBackend[] Unsupported = [IonBackend.Direct3D12, IonBackend.WebGPU];

	// Auto has no Veldrid member of the same name; it maps to Veldrid's platform default (tested separately).
	private static readonly IonBackend[] NotNamedAlike = [.. Unsupported, IonBackend.Auto];

	public static TheoryData<IonBackend> SupportedBackends()
	{
		var data = new TheoryData<IonBackend>();
		foreach (var backend in Enum.GetValues<IonBackend>())
		{
			if (!NotNamedAlike.Contains(backend)) data.Add(backend);
		}
		return data;
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[MemberData(nameof(SupportedBackends))]
	public void ToVeldrid_MapsToMemberWithSameName(IonBackend backend)
	{
		var mapped = VeldridBackendMapper.ToVeldrid(backend);

		Assert.Equal(backend.ToString(), mapped.ToString());
		Assert.Equal(Enum.Parse<VeldridBackend>(backend.ToString()), mapped);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(IonBackend.Direct3D12)]
	[InlineData(IonBackend.WebGPU)]
	public void ToVeldrid_ThrowsForBackendsVeldridDoesNotImplement(IonBackend backend)
	{
		var ex = Assert.Throws<NotSupportedException>(() => VeldridBackendMapper.ToVeldrid(backend));
		Assert.Contains(backend.ToString(), ex.Message);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ToVeldrid_MapsAutoToThePlatformDefault()
	{
		Assert.Equal(Veldrid.StartupUtilities.VeldridStartup.GetPlatformDefaultBackend(), VeldridBackendMapper.ToVeldrid(IonBackend.Auto));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EveryVeldridBackendIsReachable()
	{
		var reachable = Enum.GetValues<IonBackend>()
			.Where(backend => !Unsupported.Contains(backend))
			.Select(VeldridBackendMapper.ToVeldrid)
			.ToHashSet();

		Assert.Equal(Enum.GetValues<VeldridBackend>().ToHashSet(), reachable);
	}
}
