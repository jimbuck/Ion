namespace Ion.Extensions.Rendering3D.Tests;

/// <summary>The directional shadow fit.</summary>
public class ShadowMathTests
{
	private static readonly Camera Lens = new() { FieldOfView = MathF.PI / 3, Near = 0.5f, Far = 200f };

	private static DirectionalShadow Fit(Vector3 eye, Vector3 target, Vector3 light, ReadOnlySpan<Aabb> casters = default, int size = 1024) =>
		ShadowMath.Fit(Lens, Camera.GetViewMatrix(Transform.LookAt(eye, target)), 16 / 9f, light, 30f, size, casters);

	private static Vector3 ToShadow(in DirectionalShadow shadow, Vector3 world)
	{
		var p = Vector4.Transform(new Vector4(world, 1), shadow.ShadowMatrix);
		return new Vector3(p.X, p.Y, p.Z) / p.W;
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheCameraSliceUpToTheShadowDistanceMapsInsideTheShadowMap()
	{
		var shadow = Fit(new Vector3(0, 5, 10), Vector3.Zero, new Vector3(-0.4f, -1, -0.3f));
		var slice = Lens with { Far = 30f };
		Span<Vector3> corners = stackalloc Vector3[8];
		Frustum.GetCorners(Camera.GetViewMatrix(Transform.LookAt(new Vector3(0, 5, 10), Vector3.Zero)) * slice.GetProjectionMatrix(16 / 9f), corners);
		foreach (var corner in corners)
		{
			var s = ToShadow(shadow, corner);
			Assert.InRange(s.X, 0f, 1f);
			Assert.InRange(s.Y, 0f, 1f);
			Assert.InRange(s.Z, 0f, 1f);
		}

		Assert.Equal(2 * shadow.Radius / 1024, shadow.TexelSize, 5);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DepthGrowsAlongTheLightAndRowZeroIsTheTop()
	{
		var shadow = Fit(new Vector3(0, 5, 10), Vector3.Zero, -Vector3.UnitY);
		// Straight down: a higher point is nearer the light (smaller depth).
		var high = ToShadow(shadow, new Vector3(0, 3, 0));
		var low = ToShadow(shadow, new Vector3(0, 0, 0));
		Assert.True(high.Z < low.Z);
		Assert.Equal(high.X, low.X, 4);
		Assert.Equal(high.Y, low.Y, 4);

		// Along a slanted light: moving towards the light decreases depth.
		var slanted = Fit(new Vector3(0, 5, 10), Vector3.Zero, Vector3.Normalize(new Vector3(1, -1, 0)));
		var p = ToShadow(slanted, Vector3.Zero);
		var towardsLight = ToShadow(slanted, new Vector3(-1, 1, 0));
		Assert.True(towardsLight.Z < p.Z);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void CastersBehindTheSliceStillFitInTheDepthRange()
	{
		// A tall caster far up the light direction, outside the camera's slice.
		var caster = Aabb.FromCenterExtents(new Vector3(0, 60, 0), new Vector3(1));
		var shadow = Fit(new Vector3(0, 5, 10), Vector3.Zero, -Vector3.UnitY, [caster]);
		var top = ToShadow(shadow, new Vector3(0, 61, 0));
		Assert.InRange(top.Z, 0f, 1f);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SmallCameraMovesShiftTheShadowMapByWholeTexels()
	{
		var light = Vector3.Normalize(new Vector3(-0.4f, -1, -0.3f));
		var a = Fit(new Vector3(0, 5, 10), Vector3.Zero, light);
		var b = Fit(new Vector3(0.013f, 5, 10.007f), new Vector3(0.013f, 0, 0.007f), light);
		Assert.Equal(a.Radius, b.Radius);
		// A fixed world point moves by a whole number of texels in the map (or not at all).
		var pa = ToShadow(a, new Vector3(1, 0, 1)) * 1024;
		var pb = ToShadow(b, new Vector3(1, 0, 1)) * 1024;
		Assert.Equal(0, (pa.X - pb.X) - MathF.Round(pa.X - pb.X), 2);
		Assert.Equal(0, (pa.Y - pb.Y) - MathF.Round(pa.Y - pb.Y), 2);
	}
}
