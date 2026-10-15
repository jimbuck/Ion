namespace Ion.Extensions.Rendering3D.Tests;

/// <summary>Cascaded shadow maps on the CPU: splits, the fit of each cascade into its tile, and caster culling per cascade.</summary>
public class CascadedShadowTests
{
	private static Renderer3D NewRenderer(Action<Rendering3DOptions>? configure = null)
	{
		var options = new Rendering3DOptions { ShadowDistance = 100f };
		configure?.Invoke(options);
		return new Renderer3D(null, options) { CpuTargetSize = (160, 90) };
	}

	private static readonly Camera Lens = new() { FieldOfView = MathF.PI / 3, Near = 0.5f, Far = 300f };

	private static Transform Eye => Transform.LookAt(new Vector3(0, 6, 0), new Vector3(0, 0, -30));

	/// <summary>One frame: the camera, a sun, and a cube casting at each of <paramref name="casters"/>.</summary>
	private static void Frame(Renderer3D renderer, params Vector3[] casters)
	{
		var cube = renderer.CreateMesh(MeshPrimitives.Cube());
		renderer.BeginFrame();
		renderer.SetCamera(Lens, Eye);
		renderer.AddLight(new DirectionalLight(), new Vector3(-0.3f, -1, -0.2f));
		foreach (var p in casters) renderer.Draw(cube, default, Matrix4x4.CreateTranslation(p));
		renderer.RunCpuPipeline();
	}

	private static Vector3 ToShadow(in DirectionalShadow shadow, Vector3 world)
	{
		var p = Vector4.Transform(new Vector4(world, 1), shadow.ShadowMatrix);
		return new Vector3(p.X, p.Y, p.Z) / p.W;
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheSplitsMixEvenAndLogarithmicSpacing()
	{
		Span<float> even = stackalloc float[4];
		ShadowMath.ComputeSplits(1, 100, 0, even);
		Assert.Equal([25.75f, 50.5f, 75.25f, 100f], even.ToArray());

		Span<float> log = stackalloc float[4];
		ShadowMath.ComputeSplits(1, 100, 1, log);
		Assert.Equal(MathF.Sqrt(10), log[0], 4);
		Assert.Equal(10f, log[1], 3);
		Assert.Equal(100f, log[3]);

		Span<float> mixed = stackalloc float[4];
		ShadowMath.ComputeSplits(1, 100, 0.5f, mixed);
		for (var i = 0; i < 4; i++) Assert.Equal((even[i] + log[i]) / 2, mixed[i], 3);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void FourCascadesSliceTheViewUpToTheShadowDistanceEachInItsQuarterOfTheMap()
	{
		using var renderer = NewRenderer();
		Frame(renderer, new Vector3(0, 1, -5));
		Assert.True(renderer.ShadowActive);
		var cascades = renderer.ShadowCascades.ToArray();
		var splits = renderer.ShadowCascadeSplits.ToArray();
		Assert.Equal(4, cascades.Length);
		Assert.Equal(100f, splits[^1]);
		for (var i = 1; i < 4; i++) Assert.True(splits[i] > splits[i - 1]);
		// Near cascades cover less, so their texels are smaller.
		for (var i = 1; i < 4; i++) Assert.True(cascades[i].TexelSize > cascades[i - 1].TexelSize, $"cascade {i}");

		// Each cascade's slice of the view maps into its own tile of the 2x2 atlas.
		var view = Camera.GetViewMatrix(Eye.ToMatrix());
		Span<Vector3> corners = stackalloc Vector3[8];
		var near = Lens.EffectiveNear;
		for (var c = 0; c < 4; c++)
		{
			var slice = Lens with { Near = c == 0 ? near : splits[c - 1], Far = splits[c] };
			Frustum.GetCorners(view * slice.GetProjectionMatrix(160 / 90f), corners);
			var tile = new Vector2(c % 2, c / 2) * 0.5f;
			foreach (var corner in corners)
			{
				var s = ToShadow(cascades[c], corner);
				Assert.InRange(s.X, tile.X, tile.X + 0.5f);
				Assert.InRange(s.Y, tile.Y, tile.Y + 0.5f);
				Assert.InRange(s.Z, 0f, 1f);
			}

			Assert.Equal((uint)(c % 2 * 1024), renderer.CascadeTile(c).X);
			Assert.Equal((uint)(c / 2 * 1024), renderer.CascadeTile(c).Y);
			Assert.Equal(1024u, renderer.CascadeTile(c).Size);
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EachCascadeDrawsOnlyTheCastersItCoversAndTheStatisticsCountEachCasterOnce()
	{
		using var renderer = NewRenderer();
		// One caster close to the camera, one far down the view, one beyond the shadow distance.
		Frame(renderer, new Vector3(0, 1, -4), new Vector3(0, 1, -70), new Vector3(0, 1, -250));

		var near = Enumerable.Range(0, 4).Select(c => renderer.CascadeBatches(c).ToArray().Sum(b => b.InstanceCount)).ToArray();
		Assert.True(near[0] >= 1, "the near caster is in the first cascade");
		Assert.True(near[3] >= 1, "the far caster is in the last cascade");
		Assert.True(near[0] < 3 && near[3] < 3, string.Join(", ", near));
		Assert.Equal(2, renderer.LastFrameStatistics.ShadowCasters);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ExplicitSplitsAndOneCascadeAreHonoured()
	{
		using var renderer = NewRenderer(o => o.ShadowCascadeSplits = [0.1f, 0.3f, 0.6f]);
		Frame(renderer, new Vector3(0, 1, -5));
		var splits = renderer.ShadowCascadeSplits.ToArray();
		var near = Lens.EffectiveNear;
		Assert.Equal(near + (100 - near) * 0.1f, splits[0], 3);
		Assert.Equal(near + (100 - near) * 0.6f, splits[2], 3);
		// The last cascade always ends at the shadow distance.
		Assert.Equal(100f, splits[3]);

		using var single = NewRenderer(o => o.ShadowCascades = 1);
		Frame(single, new Vector3(0, 1, -5));
		var cascade = Assert.Single(single.ShadowCascades.ToArray());
		Assert.Equal(2048u, single.CascadeTile(0).Size);
		// A single cascade is the classic fit over the whole map.
		var fit = ShadowMath.Fit(Lens, Camera.GetViewMatrix(Eye.ToMatrix()), 160 / 90f, new Vector3(-0.3f, -1, -0.2f), 100f, 2048, [Aabb.FromCenterExtents(new Vector3(0, 1, -5), new Vector3(0.5f))]);
		Assert.Equal(fit.Radius, cascade.Radius);
		Assert.Equal(fit.ShadowMatrix, cascade.ShadowMatrix);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ACascadeStartsWhereThePreviousOneStartsBlending()
	{
		// With a blend of half of each cascade, a point in the second half of cascade 0 (the blend region) also maps into
		// cascade 1's tile, so both can be sampled there.
		using var renderer = NewRenderer(o => o.ShadowCascadeBlend = 0.5f);
		Frame(renderer, new Vector3(0, 1, -5));
		var splits = renderer.ShadowCascadeSplits.ToArray();
		var cascades = renderer.ShadowCascades.ToArray();
		var depth = Lens.EffectiveNear + (splits[0] - Lens.EffectiveNear) * 0.8f;
		var eye = Eye;
		var point = eye.Position + Vector3.Transform(-Vector3.UnitZ, eye.Rotation) * depth;
		var s = ToShadow(cascades[1], point);
		Assert.InRange(s.X, 0.5f, 1f);
		Assert.InRange(s.Y, 0f, 0.5f);
	}
}
