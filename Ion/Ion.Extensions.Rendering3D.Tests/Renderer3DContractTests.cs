using System.Reflection;

using Ion.Extensions.Assets;
using Ion.Extensions.Graphics.GLES;
using Ion.Extensions.Graphics.Rhi;
using Ion.Extensions.Graphics.Rhi.Tests;

namespace Ion.Extensions.Rendering3D.Tests;

/// <summary>What a <see cref="Scripted3DSystem"/> does.</summary>
public sealed class Scene3DScript
{
	public Action<IRenderer3D, IAssetManager>? Init { get; set; }
	public Action<IRenderer3D, ISpriteBatch>? Render { get; set; }
}

/// <summary>Runs a <see cref="Scene3DScript"/> in Init and Render.</summary>
public sealed class Scripted3DSystem(Scene3DScript script, IRenderer3D renderer, ISpriteBatch sprites, IAssetManager assets)
{
	[Init]
	public void Init(GameTime dt) => script.Init?.Invoke(renderer, assets);

	[Render]
	public void Render(GameTime dt) => script.Render?.Invoke(renderer, sprites);
}

/// <summary>
/// The 3D renderer end to end on a headless RHI backend with validation on (64x64 offscreen frames): passes, materials,
/// shadows, skybox orientation, render targets, cameras, custom material shaders, blending and the 2D overlay. Each
/// backend derives a sealed class marked with <see cref="RhiBackendAttribute"/>.
/// </summary>
public abstract class Renderer3DContractTests
{
	private const uint Size = 64;

	/// <summary>The backend of the concrete test class.</summary>
	protected GraphicsBackend Backend => GetType().GetCustomAttribute<RhiBackendAttribute>()?.Backend
		?? throw new InvalidOperationException($"{GetType().Name} needs an [RhiBackend] attribute.");

	/// <summary>Configures a host to render headless on <see cref="Backend"/>.</summary>
	protected virtual IonTestHost ConfigureHost(IonTestHost host) => host.WithConfiguration("Ion:Graphics:PreferredBackend", Backend.ToString());

	private Screenshot Render(Scene3DScript script, int frames = 1, Action<IonTestHost>? inspect = null, Action<Rendering3DOptions>? options = null)
	{
		var log = new ErrorLog();
		Screenshot shot;
		var host = ConfigureHost(log.Attach(new IonTestHost().WithRendering(Size, Size)));
		using (host
			.Configure(services =>
			{
				services.AddSingleton(script).AddRendering3D();
				if (options is not null) services.Configure(options);
			})
			.ConfigureApp(app => app.UseRendering3D())
			.WithSystem<Scripted3DSystem>())
		{
			host.Step(frames);
			Assert.True(host.Get<Renderer3D>().HasDevice);
			Assert.Equal(Backend, host.Get<IGraphicsFrame>().Device.Backend);
			shot = host.Screenshot();
			inspect?.Invoke(host);
		}

		log.AssertClean();
		return shot;
	}

	private static void AssertPixel(Screenshot shot, int x, int y, int r, int g, int b, int tolerance = 3)
	{
		var p = shot.GetPixel(x, y);
		Assert.True(Math.Abs(p.R - r) <= tolerance && Math.Abs(p.G - g) <= tolerance && Math.Abs(p.B - b) <= tolerance,
			$"Pixel ({x}, {y}) is {p}, expected ({r}, {g}, {b}) within {tolerance}.");
	}

	private static Transform LookAtOrigin => Transform.LookAt(new Vector3(0, 0, 5), Vector3.Zero);

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void AnUnlitCubeCoversTheCenterOverTheClearColor()
	{
		MeshHandle cube = default;
		MaterialHandle red = default;
		var shot = Render(new Scene3DScript
		{
			Init = (r, _) =>
			{
				cube = r.CreateMesh(MeshPrimitives.Cube(2));
				red = r.CreateMaterial(new UnlitMaterial(Color.Red));
			},
			Render = (r, _) =>
			{
				r.SetCamera(new Camera { ClearColor = Color.Blue }, LookAtOrigin);
				r.Draw(cube, red, Matrix4x4.Identity);
			},
		}, inspect: host =>
		{
			var stats = host.Get<IRenderer3D>().LastFrameStatistics;
			Assert.Equal(1, stats.Visible);
			Assert.Equal(1, stats.DrawCalls);
			Assert.Equal(12, stats.Triangles);
			// The frame stats add the 3D renderer's draws to the sprite batch's.
			Assert.True(host.LastFrame.DrawCalls >= 1);
		});

		AssertPixel(shot, 32, 32, 255, 0, 0);
		AssertPixel(shot, 2, 2, 0, 0, 255);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void PbrLightingFacesTheLightAndShadowsDarkenWhatIsBehindACaster()
	{
		// A white ground plane seen from above, a box floating over its left half, the sun straight down: the ground under
		// the box is in shadow, the right half is lit.
		MeshHandle plane = default, box = default;
		MaterialHandle white = default;
		var shot = Render(new Scene3DScript
		{
			Init = (r, _) =>
			{
				plane = r.CreateMesh(MeshPrimitives.Plane(10));
				box = r.CreateMesh(MeshPrimitives.Cube(1));
				white = r.CreateMaterial(new PbrMaterial(Color.White, roughness: 0.8f));
				r.SetEnvironment(new SceneEnvironment { AmbientColor = Color.White, AmbientIntensity = 0.05f });
			},
			Render = (r, _) =>
			{
				r.SetCamera(new Camera { Projection = ProjectionKind.Orthographic, OrthographicSize = 2.5f, Near = 0.1f, Far = 50f, CullingMask = 0b01 }, Transform.LookAt(new Vector3(0, 10, 0), Vector3.Zero, -Vector3.UnitZ));
				r.AddLight(new DirectionalLight(Color.White, 2f), -Vector3.UnitY);
				r.Draw(plane, white, Matrix4x4.Identity);
				// The box: not seen by the camera (it is culled from the view by its layer), but casting.
				r.Submit(new MeshRenderer(box, white) { LayerMask = 0b10 }, Matrix4x4.CreateScale(2) * Matrix4x4.CreateTranslation(-1.2f, 3, 0));
			},
		}, inspect: host => Assert.True(host.Get<Renderer3D>().ShadowActive));

		var lit = shot.GetPixel(52, 32);
		var shadowed = shot.GetPixel(12, 32);
		Assert.True(lit.R > 200, $"lit ground is {lit}");
		Assert.True(shadowed.R < 90, $"shadowed ground is {shadowed}");
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void AShadowCastingLightAfterANonCastingOneStillCastsShadows()
	{
		// The same scene as above, but a blue light that does not cast shadows is submitted first. The white sun that casts
		// comes second and must own the shadow map: the red channel of the ground under the box stays dark.
		MeshHandle plane = default, box = default;
		MaterialHandle white = default;
		var shot = Render(new Scene3DScript
		{
			Init = (r, _) =>
			{
				plane = r.CreateMesh(MeshPrimitives.Plane(10));
				box = r.CreateMesh(MeshPrimitives.Cube(1));
				white = r.CreateMaterial(new PbrMaterial(Color.White, roughness: 0.8f));
				r.SetEnvironment(new SceneEnvironment { AmbientColor = Color.White, AmbientIntensity = 0.05f });
			},
			Render = (r, _) =>
			{
				r.SetCamera(new Camera { Projection = ProjectionKind.Orthographic, OrthographicSize = 2.5f, Near = 0.1f, Far = 50f, CullingMask = 0b01 }, Transform.LookAt(new Vector3(0, 10, 0), Vector3.Zero, -Vector3.UnitZ));
				r.AddLight(new DirectionalLight(Color.Blue, 1f, castShadows: false), -Vector3.UnitY);
				r.AddLight(new DirectionalLight(Color.White, 2f), -Vector3.UnitY);
				r.Draw(plane, white, Matrix4x4.Identity);
				r.Submit(new MeshRenderer(box, white) { LayerMask = 0b10 }, Matrix4x4.CreateScale(2) * Matrix4x4.CreateTranslation(-1.2f, 3, 0));
			},
		}, inspect: host => Assert.True(host.Get<Renderer3D>().ShadowActive));

		var lit = shot.GetPixel(52, 32);
		var shadowed = shot.GetPixel(12, 32);
		Assert.True(lit.R > 200, $"lit ground is {lit}");
		Assert.True(shadowed.R < 90, $"shadowed ground is {shadowed}");
		// The non-casting blue light still lights the shadowed ground.
		Assert.True(shadowed.B > shadowed.R + 40, $"shadowed ground is {shadowed}");
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void TheSkyboxShowsTheFrontFaceAheadAndTheRightFaceOnTheRight()
	{
		// Six single-color faces and four cameras in the four quadrants, looking ahead (-Z), right (+X), up (+Y) and left
		// (-X): ahead is the front (+Z) face, right the +X face, up the +Y face, left the -X face.
		var shot = Render(new Scene3DScript
		{
			Init = (r, _) =>
			{
				var renderer = (Renderer3D)r;
				byte[][] faces =
				[
					Face(255, 0, 0), Face(0, 255, 255), Face(0, 255, 0), Face(255, 0, 255), Face(0, 0, 255), Face(255, 255, 0),
				];
				var cube = CubemapLoader.Create(renderer, "faces", 4, faces);
				r.SetEnvironment(new SceneEnvironment { Skybox = cube.Handle, SkyboxIntensity = 1, AmbientIntensity = 0 });
			},
			Render = (r, _) =>
			{
				Look(r, new RectangleF(0, 0, 0.5f, 0.5f), -Vector3.UnitZ);
				Look(r, new RectangleF(0.5f, 0, 0.5f, 0.5f), Vector3.UnitX);
				Look(r, new RectangleF(0, 0.5f, 0.5f, 0.5f), Vector3.UnitY);
				Look(r, new RectangleF(0.5f, 0.5f, 0.5f, 0.5f), -Vector3.UnitX);
			},
		});

		AssertPixel(shot, 16, 16, 0, 0, 255, tolerance: 6);   // ahead: +Z (front)
		AssertPixel(shot, 48, 16, 255, 0, 0, tolerance: 6);   // right: +X
		AssertPixel(shot, 16, 48, 0, 255, 0, tolerance: 6);   // up: +Y
		AssertPixel(shot, 48, 48, 0, 255, 255, tolerance: 6); // left: -X

		static void Look(IRenderer3D r, RectangleF viewport, Vector3 direction) =>
			r.AddCamera(new Camera { FieldOfView = MathF.PI / 3, Clear = CameraClear.Skybox, Viewport = viewport }, Transform.LookAt(Vector3.Zero, direction).ToMatrix());

		static byte[] Face(byte r, byte g, byte b)
		{
			var face = new byte[4 * 4 * 4];
			for (var i = 0; i < face.Length; i += 4) (face[i], face[i + 1], face[i + 2], face[i + 3]) = (r, g, b, 255);
			return face;
		}
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void ACameraRendersIntoATargetThatAMaterialSamples()
	{
		MeshHandle cube = default, quad = default;
		MaterialHandle green = default, screen = default;
		RenderTargetHandle target = default;
		var shot = Render(new Scene3DScript
		{
			Init = (r, _) =>
			{
				cube = r.CreateMesh(MeshPrimitives.Cube(2));
				quad = r.CreateMesh(MeshPrimitives.Plane(2));
				green = r.CreateMaterial(new UnlitMaterial(Color.Lime));
				target = r.CreateRenderTarget(32, 32, "Monitor");
				screen = r.CreateMaterial(new UnlitMaterial(Color.White, r.GetRenderTargetTexture(target)));
			},
			Render = (r, _) =>
			{
				// The monitor camera (priority -1, first) sees a green cube on yellow; the main camera sees the monitor.
				r.AddCamera(new Camera { Priority = -1, Target = target, ClearColor = Color.Yellow, CullingMask = 0b10 }, LookAtOrigin.ToMatrix());
				r.Submit(new MeshRenderer(cube, green) { LayerMask = 0b10 }, Matrix4x4.Identity);
				r.AddCamera(new Camera { CullingMask = 0b01, ClearColor = Color.Black, Projection = ProjectionKind.Orthographic, OrthographicSize = 1f }, Transform.LookAt(new Vector3(0, 5, 0), Vector3.Zero, -Vector3.UnitZ).ToMatrix());
				r.Draw(quad, screen, Matrix4x4.Identity);
			},
		}, frames: 2);

		AssertPixel(shot, 32, 32, 0, 255, 0);
		AssertPixel(shot, 3, 3, 255, 255, 0);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void AMaterialSamplingTheTargetItIsDrawnIntoIsSkippedThere()
	{
		// The monitor camera sees the monitor itself: drawing it there would sample the texture being rendered into.
		MeshHandle quad = default;
		MaterialHandle screen = default;
		RenderTargetHandle target = default;
		var shot = Render(new Scene3DScript
		{
			Init = (r, _) =>
			{
				quad = r.CreateMesh(MeshPrimitives.Plane(2));
				target = r.CreateRenderTarget(32, 32);
				screen = r.CreateMaterial(new UnlitMaterial(Color.White, r.GetRenderTargetTexture(target)));
			},
			Render = (r, _) =>
			{
				var above = Transform.LookAt(new Vector3(0, 5, 0), Vector3.Zero, -Vector3.UnitZ).ToMatrix();
				r.AddCamera(new Camera { Priority = -1, Target = target, ClearColor = Color.Red, Projection = ProjectionKind.Orthographic, OrthographicSize = 1f }, above);
				r.AddCamera(new Camera { ClearColor = Color.Black, Projection = ProjectionKind.Orthographic, OrthographicSize = 1f }, above);
				r.Draw(quad, screen, Matrix4x4.Identity);
			},
		}, frames: 2);

		// The frame shows the monitor, which shows the target's clear color only.
		AssertPixel(shot, 32, 32, 255, 0, 0);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void TwoCamerasSplitTheFrameAndTheSecondClearsOnlyItsViewport()
	{
		MeshHandle cube = default;
		MaterialHandle red = default;
		var shot = Render(new Scene3DScript
		{
			Init = (r, _) =>
			{
				cube = r.CreateMesh(MeshPrimitives.Cube(2));
				red = r.CreateMaterial(new UnlitMaterial(Color.Red));
			},
			Render = (r, _) =>
			{
				r.AddCamera(new Camera { Viewport = new RectangleF(0, 0, 0.5f, 1), ClearColor = Color.Blue }, LookAtOrigin.ToMatrix());
				r.AddCamera(new Camera { Viewport = new RectangleF(0.5f, 0, 0.5f, 1), ClearColor = Color.Lime, Priority = 1 }, Transform.LookAt(new Vector3(0, 0, -5), new Vector3(0, 0, -10)).ToMatrix());
				r.Draw(cube, red, Matrix4x4.Identity);
			},
		});

		AssertPixel(shot, 16, 32, 255, 0, 0);  // the left camera sees the cube
		AssertPixel(shot, 1, 1, 0, 0, 255);    // on its blue
		AssertPixel(shot, 48, 32, 0, 255, 0);  // the right camera looks away: only its clear color
		AssertPixel(shot, 62, 1, 0, 255, 0);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void ACustomMaterialShaderDrawsThroughTheSamePasses()
	{
		MeshHandle plane = default;
		MaterialHandle stripes = default;
		var shot = Render(new Scene3DScript
		{
			Init = (r, _) =>
			{
				plane = r.CreateMesh(MeshPrimitives.Plane(4));
				var shader = r.CreateMaterialShader(new MaterialShaderDescriptor
				{
					Name = "stripes",
					Assembly = typeof(Renderer3DContractTests).Assembly,
					FragmentShader = "stripes.frag",
					UniformSize = 32,
				});
				var colors = new Vector4[] { new(1, 0, 0, 1), new(0, 0, 1, 1) };
				stripes = r.CreateMaterial(shader, System.Runtime.InteropServices.MemoryMarshal.AsBytes(colors.AsSpan()), []);
			},
			Render = (r, _) =>
			{
				r.SetCamera(new Camera { Projection = ProjectionKind.Orthographic, OrthographicSize = 2f }, Transform.LookAt(new Vector3(0, 5, 0), Vector3.Zero, -Vector3.UnitZ));
				r.Draw(plane, stripes, Matrix4x4.Identity);
			},
		});

		// World x in [-2, 2] across the frame: [-2, -1) red, [-1, 0) blue, [0, 1) red, [1, 2) blue.
		AssertPixel(shot, 8, 32, 255, 0, 0);
		AssertPixel(shot, 24, 32, 0, 0, 255);
		AssertPixel(shot, 40, 32, 255, 0, 0);
		AssertPixel(shot, 56, 32, 0, 0, 255);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void BlendedObjectsBlendOverOpaqueOnesAndTheOverlayDrawsOnTop()
	{
		MeshHandle cube = default, plane = default;
		MaterialHandle red = default, glass = default;
		var shot = Render(new Scene3DScript
		{
			Init = (r, _) =>
			{
				cube = r.CreateMesh(MeshPrimitives.Cube(2));
				plane = r.CreateMesh(MeshPrimitives.Plane(1));
				red = r.CreateMaterial(new UnlitMaterial(Color.Red));
				glass = r.CreateMaterial(new UnlitMaterial(new Color(0f, 0f, 1f, 0.5f)) { AlphaMode = AlphaMode.Blend, DoubleSided = true });
			},
			Render = (r, sprites) =>
			{
				r.SetCamera(new Camera { ClearColor = Color.Black }, LookAtOrigin);
				r.Draw(cube, red, Matrix4x4.Identity);
				// A half-transparent blue quad in front of the cube, facing the camera.
				r.Draw(plane, glass, Matrix4x4.CreateRotationX(MathF.PI / 2) * Matrix4x4.CreateTranslation(0, 0, 2));
				sprites.DrawRect(Color.White, new RectangleF(0, 0, 8, 8));
			},
		});

		AssertPixel(shot, 32, 32, 128, 0, 128, tolerance: 4); // blue at half over red
		AssertPixel(shot, 3, 3, 255, 255, 255);              // the sprite on top
		AssertPixel(shot, 3, 60, 0, 0, 0);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void TheDepthPrepassRendersTheSameImage()
	{
		Scene3DScript Scene()
		{
			MeshHandle sphere = default, plane = default;
			MaterialHandle gold = default, ground = default;
			return new Scene3DScript
			{
				Init = (r, _) =>
				{
					sphere = r.CreateMesh(MeshPrimitives.Sphere(1));
					plane = r.CreateMesh(MeshPrimitives.Plane(10));
					gold = r.CreateMaterial(new PbrMaterial(new Color(0xE6, 0xB8, 0x5C), metallic: 1, roughness: 0.3f));
					ground = r.CreateMaterial(new PbrMaterial(Color.Gray));
				},
				Render = (r, _) =>
				{
					r.SetCamera(new Camera(), Transform.LookAt(new Vector3(0, 2, 5), Vector3.Zero));
					r.AddLight(new DirectionalLight(Color.White, 2), new Vector3(-0.5f, -1, -0.3f));
					r.AddLight(new PointLight(Color.Orange, 3, 5), new Vector3(1, 1.5f, 1));
					r.Draw(plane, ground, Matrix4x4.CreateTranslation(0, -1, 0));
					r.Draw(sphere, gold, Matrix4x4.Identity);
				},
			};
		}

		var without = Render(Scene());
		var with = Render(Scene(), options: o => o.DepthPrepass = true);
		var comparison = GoldenImage.Compare(with, without, tolerance: 1);
		Assert.True(comparison.MismatchedPixels == 0, comparison.ToString());
		AssertPixel(without, 32, 32, without.GetPixel(32, 32).R, without.GetPixel(32, 32).G, without.GetPixel(32, 32).B);
		Assert.NotEqual(without.GetPixel(32, 32), without.GetPixel(2, 2));
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void SkinnedMeshesRenderAsTheRigidMeshesTheirJointsPlace()
	{
		// Two cubes whose vertices follow joint 0, posed by their palettes (the second cube's palette after the first's in
		// the frame), against the same cubes drawn rigid at those matrices. A sun casts their shadows on a plane, so the
		// skinned shadow and (with the prepass) depth pipelines draw too.
		var first = Matrix4x4.CreateRotationY(0.6f) * Matrix4x4.CreateTranslation(-0.9f, 0, 0);
		var second = Matrix4x4.CreateScale(0.7f) * Matrix4x4.CreateRotationX(0.4f) * Matrix4x4.CreateTranslation(1.1f, 0.3f, 0);
		Scene3DScript Scene(bool skinned)
		{
			MeshHandle cube = default, plane = default;
			MaterialHandle red = default, ground = default;
			return new Scene3DScript
			{
				Init = (r, _) =>
				{
					var data = MeshPrimitives.Cube(1);
					if (skinned)
					{
						data.Joints = [.. data.Positions.Select(_ => new JointIndices(0, 0, 0, 0))];
						data.Weights = [.. data.Positions.Select(_ => Vector4.UnitX)];
					}

					cube = r.CreateMesh(data);
					plane = r.CreateMesh(MeshPrimitives.Plane(10));
					red = r.CreateMaterial(new PbrMaterial(new Color(0xD0, 0x40, 0x30), roughness: 0.6f));
					ground = r.CreateMaterial(new PbrMaterial(Color.Gray));
				},
				Render = (r, _) =>
				{
					r.SetCamera(new Camera(), Transform.LookAt(new Vector3(0, 2, 5), Vector3.Zero));
					r.AddLight(new DirectionalLight(Color.White, 2), new Vector3(-0.4f, -1, -0.5f));
					r.Draw(plane, ground, Matrix4x4.CreateTranslation(0, -1, 0));
					var renderer = new MeshRenderer(cube, red);
					if (skinned)
					{
						r.Submit(renderer, Matrix4x4.Identity, [first]);
						r.Submit(renderer, Matrix4x4.Identity, [second, Matrix4x4.Identity]);
					}
					else
					{
						r.Submit(renderer, first);
						r.Submit(renderer, second);
					}
				},
			};
		}

		var rigid = Render(Scene(skinned: false));
		var skinned = Render(Scene(skinned: true), inspect: host => Assert.Equal(2, host.Get<Renderer3D>().Skins.Length));
		var comparison = GoldenImage.Compare(skinned, rigid, tolerance: 2);
		Assert.True(comparison.MismatchedPixels == 0, comparison.ToString());
		var prepass = Render(Scene(skinned: true), options: o => o.DepthPrepass = true);
		comparison = GoldenImage.Compare(prepass, rigid, tolerance: 2);
		Assert.True(comparison.MismatchedPixels == 0, comparison.ToString());

		// The cubes are in the picture (red), away from the middle where an unposed cube would be.
		var red = 0;
		for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++) if (skinned.GetPixel(x, y) is var p && p.R > p.G + 40 && p.R > p.B + 40) red++;
		Assert.True(red > 100, $"{red} red pixels");
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void CascadesShadowTheGroundNearAndFarAndTheTintShowsWhichIsWhich()
	{
		// A camera low over a long white ground, looking down it; the sun straight down. Two boxes the camera does not see
		// (another layer) cast square shadows, one near and one 60 units away: both land in the picture, in different
		// cascades.
		var camera = new Camera { Near = 0.5f, Far = 200f, CullingMask = 0b01 };
		var eye = Transform.LookAt(new Vector3(0, 6, 0), new Vector3(0, 0, -30));
		Vector3 nearSpot = new(0, 0, -9), farSpot = new(0, 0, -60);
		Scene3DScript Scene()
		{
			MeshHandle plane = default, box = default;
			MaterialHandle white = default;
			return new Scene3DScript
			{
				Init = (r, _) =>
				{
					plane = r.CreateMesh(MeshPrimitives.Plane(400));
					box = r.CreateMesh(MeshPrimitives.Cube(1));
					white = r.CreateMaterial(new PbrMaterial(Color.White, roughness: 0.9f));
					r.SetEnvironment(new SceneEnvironment { AmbientColor = Color.White, AmbientIntensity = 0.05f });
				},
				Render = (r, _) =>
				{
					r.SetCamera(camera, eye);
					r.AddLight(new DirectionalLight(Color.White, 2f), -Vector3.UnitY);
					r.Draw(plane, white, Matrix4x4.Identity);
					r.Submit(new MeshRenderer(box, white) { LayerMask = 0b10 }, Matrix4x4.CreateScale(2.5f) * Matrix4x4.CreateTranslation(nearSpot + new Vector3(0, 3, 0)));
					r.Submit(new MeshRenderer(box, white) { LayerMask = 0b10 }, Matrix4x4.CreateScale(8f) * Matrix4x4.CreateTranslation(farSpot + new Vector3(0, 6, 0)));
				},
			};
		}

		// Where a ground point lands in the 64x64 picture.
		var viewProjection = Camera.GetViewMatrix(eye.ToMatrix()) * camera.GetProjectionMatrix(1f);
		(int X, int Y) Pixel(Vector3 world)
		{
			var clip = Vector4.Transform(new Vector4(world, 1), viewProjection);
			return ((int)((clip.X / clip.W * 0.5f + 0.5f) * Size), (int)((0.5f - clip.Y / clip.W * 0.5f) * Size));
		}

		var shot = Render(Scene(), options: o => o.ShadowDistance = 100f, inspect: host =>
		{
			var renderer = host.Get<Renderer3D>();
			Assert.Equal(4, renderer.ShadowCascades.Length);
			Assert.Equal(3, renderer.LastFrameStatistics.ShadowCasters);   // the boxes and the ground
		});

		foreach (var spot in new[] { nearSpot, farSpot })
		{
			var (x, y) = Pixel(spot);
			var (lx, ly) = Pixel(spot + new Vector3(spot == nearSpot ? 4 : 12, 0, 0));
			var shadowed = shot.GetPixel(x, y);
			var lit = shot.GetPixel(lx, ly);
			Assert.True(shadowed.R < 90, $"the ground under the box at {spot} is {shadowed} at ({x}, {y})");
			Assert.True(lit.R > 180, $"the ground beside the box at {spot} is {lit} at ({lx}, {ly})");
		}

		// With the tint on and splits at 20, 40 and 70 units, the near ground is red (the first cascade) and the far ground
		// blue (the third).
		var tinted = Render(Scene(), options: o => { o.ShadowDistance = 100f; o.ShadowCascadeSplits = [0.2f, 0.4f, 0.7f]; o.ShowShadowCascades = true; });
		var (nx, ny) = Pixel(nearSpot + new Vector3(4, 0, 0));
		var near = tinted.GetPixel(nx, ny);
		Assert.True(near.R > near.G + 30 && near.R > near.B + 30, $"the near ground is {near}");
		var (fx, fy) = Pixel(farSpot + new Vector3(12, 0, 0));
		var far = tinted.GetPixel(fx, fy);
		Assert.True(far.B > far.R + 30 && far.B > far.G + 30, $"the far ground is {far}");
	}

	/// <summary>A lit scene with edges: a turned PBR cube on a plane, a sun with shadows and a sky-blue clear color.</summary>
	private static Scene3DScript LitScene()
	{
		MeshHandle cube = default, plane = default;
		MaterialHandle red = default, ground = default;
		return new Scene3DScript
		{
			Init = (r, _) =>
			{
				cube = r.CreateMesh(MeshPrimitives.Cube(1.4f));
				plane = r.CreateMesh(MeshPrimitives.Plane(10));
				red = r.CreateMaterial(new PbrMaterial(new Color(0xD0, 0x40, 0x30), roughness: 0.6f));
				ground = r.CreateMaterial(new PbrMaterial(Color.Gray));
			},
			Render = (r, _) =>
			{
				r.SetCamera(new Camera { ClearColor = new Color(0x60, 0x90, 0xC0) }, Transform.LookAt(new Vector3(0, 2, 5), Vector3.Zero));
				r.AddLight(new DirectionalLight(Color.White, 2), new Vector3(-0.4f, -1, -0.5f));
				r.Draw(plane, ground, Matrix4x4.CreateTranslation(0, -1, 0));
				r.Draw(cube, red, Matrix4x4.CreateRotationY(0.5f) * Matrix4x4.CreateRotationX(0.3f));
			},
		};
	}

	/// <summary>One emissive cube (no light) on black: its color is <paramref name="intensity"/> times white.</summary>
	private static Scene3DScript GlowingCube(float intensity, float size = 1.2f)
	{
		MeshHandle cube = default;
		MaterialHandle glow = default;
		return new Scene3DScript
		{
			Init = (r, _) =>
			{
				cube = r.CreateMesh(MeshPrimitives.Cube(size));
				glow = r.CreateMaterial(new PbrMaterial(Color.Black) { Emissive = Color.White, EmissiveIntensity = intensity });
				r.SetEnvironment(new SceneEnvironment { AmbientIntensity = 0 });
			},
			Render = (r, _) =>
			{
				r.SetCamera(new Camera { ClearColor = Color.Black }, LookAtOrigin);
				r.Draw(cube, glow, Matrix4x4.Identity);
			},
		};
	}

	private static int PartlyCovered(Screenshot shot, Func<Rgba8, bool> partial)
	{
		var count = 0;
		for (var y = 0; y < Size; y++) for (var x = 0; x < Size; x++) if (partial(shot.GetPixel(x, y))) count++;
		return count;
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void HdrWithoutAToneCurveRendersAsLowDynamicRange()
	{
		// The HDR scene texture, composited with exposure 1 and no curve, is the image drawn straight into the frame.
		var ldr = Render(LitScene());
		var hdr = Render(LitScene(), inspect: host => Assert.True(host.Get<Renderer3D>().PostActive), options: o =>
		{
			o.Hdr = true;
			o.ToneMapping = ToneMapping.None;
		});
		var comparison = GoldenImage.Compare(hdr, ldr, tolerance: 3);
		Assert.True(comparison.MismatchedPixels == 0, comparison.ToString());
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void ToneMappingCompressesBrightnessAboveOne()
	{
		// An emissive cube 4 times brighter than white: clipped to white in LDR, mapped by the curve with HDR.
		static int Expected(float linear) => (int)MathF.Round(ColorSpace.LinearToSrgb(Math.Clamp(linear, 0f, 1f)) * 255f);
		var clipped = Render(GlowingCube(4f));
		Assert.Equal(255, clipped.GetPixel(32, 32).R);

		var reinhard = Render(GlowingCube(4f), options: o => { o.Hdr = true; o.ToneMapping = ToneMapping.Reinhard; });
		AssertPixel(reinhard, 32, 32, Expected(4f / 5f), Expected(4f / 5f), Expected(4f / 5f), tolerance: 4);

		var aces = Render(GlowingCube(4f), options: o => { o.Hdr = true; o.ToneMapping = ToneMapping.Aces; o.Exposure = 0.5f; });
		var x = 2f;   // 4 times an exposure of 0.5
		var mapped = x * (2.51f * x + 0.03f) / (x * (2.43f * x + 0.59f) + 0.14f);
		AssertPixel(aces, 32, 32, Expected(mapped), Expected(mapped), Expected(mapped), tolerance: 4);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void BloomSpreadsLightAroundBrightObjects()
	{
		// A small, very bright cube on black: without bloom the frame around it stays black; with bloom it glows.
		var plain = Render(GlowingCube(8f, size: 0.6f), options: o => o.Hdr = true);
		var bloom = Render(GlowingCube(8f, size: 0.6f), options: o => { o.Hdr = true; o.Bloom = true; o.BloomIntensity = 1f; });
		Assert.True(plain.GetPixel(32, 20).R < 4, $"no bloom: {plain.GetPixel(32, 20)}");
		Assert.True(bloom.GetPixel(32, 20).R > 12, $"bloom: {bloom.GetPixel(32, 20)}");
		// It fades with distance from the cube.
		Assert.True(bloom.GetPixel(32, 20).R > bloom.GetPixel(32, 6).R, $"{bloom.GetPixel(32, 20)} near, {bloom.GetPixel(32, 6)} far");
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void MsaaSmoothsEdgesAndLeavesTheRestOfTheImageAlone()
	{
		// With 4 samples, shadows and the depth prepass the cube's silhouette gets partly covered pixels (between the red of
		// the cube and the blue sky); away from edges the image is the one without MSAA.
		static bool Blend(Rgba8 p) => p.R > 0x70 && p.R < 0xC0 && p.B > 0x50 && p.B < 0xB0 && p.G < 0x80;
		var hard = Render(LitScene());
		var smooth = Render(LitScene(), options: o => { o.Msaa = 4; o.DepthPrepass = true; });
		Assert.True(PartlyCovered(smooth, Blend) > PartlyCovered(hard, Blend) + 4, $"{PartlyCovered(smooth, Blend)} blended edge pixels with MSAA, {PartlyCovered(hard, Blend)} without");
		var comparison = GoldenImage.Compare(smooth, hard, tolerance: 6);
		Assert.True(comparison.MismatchedPixels < Size * Size / 10, comparison.ToString());

		// More samples than any device has is clamped, not refused.
		var many = Render(LitScene(), options: o => o.Msaa = 64);
		Assert.True(GoldenImage.Compare(many, hard, tolerance: 6).MismatchedPixels < Size * Size / 10);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void TheSampleModelLoadsAndRendersWithItsTextures()
	{
		IModel model = null!;
		var shot = Render(new Scene3DScript
		{
			Init = (r, assets) =>
			{
				model = assets.Load<IModel>("Avocado/Avocado.gltf");
				r.SetEnvironment(new SceneEnvironment { AmbientColor = Color.White, AmbientIntensity = 0.4f });
			},
			Render = (r, _) =>
			{
				r.SetCamera(new Camera { ClearColor = Color.Black, FieldOfView = MathF.PI / 4 }, Transform.LookAt(new Vector3(0, 0.035f, -0.12f), new Vector3(0, 0.03f, 0)));
				r.AddLight(new DirectionalLight(Color.White, 2), new Vector3(0.2f, -0.5f, 1f));
				r.Draw(model, Matrix4x4.Identity);
			},
		});

		Assert.Equal(3, model.Textures.Count);
		// The cut face (seen from -Z after the node's half turn) is textured: green flesh around a brown pit.
		var center = shot.GetPixel(32, 34);
		Assert.True(center.R > 40 && center.R > center.B, $"the pit is {center}");
		var covered = 0;
		for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++) if (shot.GetPixel(x, y).G > 20) covered++;
		Assert.InRange(covered, 400, 3500);
	}
}

/// <summary>The renderer contract on Vulkan.</summary>
[RhiBackend(GraphicsBackend.Vulkan)]
public sealed class VulkanRenderer3DTests : Renderer3DContractTests;

/// <summary>The renderer contract on OpenGL ES at the driver's feature level.</summary>
[RhiBackend(GraphicsBackend.OpenGLES)]
public sealed class GlesRenderer3DTests : Renderer3DContractTests;

/// <summary>The renderer contract on the OpenGL ES 3.0 fallback (GLSL ES 3.00, attribute pointers per draw).</summary>
[RhiBackend(GraphicsBackend.OpenGLES)]
public sealed class GlesEs30Renderer3DTests : Renderer3DContractTests
{
	protected override IonTestHost ConfigureHost(IonTestHost host) => base.ConfigureHost(host).WithConfiguration("Ion:Graphics:Gles:MaxFeatureLevel", nameof(GlesFeatureLevel.Es30));
}

/// <summary>The renderer contract on the OpenGL ES 3.1 paths (the R36S's Mali-G31 level).</summary>
[RhiBackend(GraphicsBackend.OpenGLES)]
public sealed class GlesEs31Renderer3DTests : Renderer3DContractTests
{
	protected override IonTestHost ConfigureHost(IonTestHost host) => base.ConfigureHost(host).WithConfiguration("Ion:Graphics:Gles:MaxFeatureLevel", nameof(GlesFeatureLevel.Es31));
}
