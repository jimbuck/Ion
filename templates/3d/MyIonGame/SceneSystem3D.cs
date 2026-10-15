using System.Numerics;

using Ion;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;

namespace MyIonGame;

/// <summary>
/// A row of spinning cubes on a ground plane, lit by a sun with shadows. The 3D renderer is immediate mode: create meshes
/// and materials once (Init), then submit the camera, lights and draws every frame (Render). Headless without a GPU the
/// renderer still runs its CPU pipeline (culling, batching), so <c>IRenderer3D.LastFrameStatistics</c> works in tests.
/// </summary>
public sealed class SceneSystem3D(IRenderer3D renderer, SpinState spin, GameSettings settings)
{
	private MeshHandle _cube;
	private MeshHandle _ground;
	private MaterialHandle _cubeMaterial;
	private MaterialHandle _groundMaterial;

	/// <summary>Creates the meshes and materials.</summary>
	[Init]
	public void Load(GameTime dt)
	{
		_cube = renderer.CreateMesh(MeshPrimitives.Cube(1f));
		_ground = renderer.CreateMesh(MeshPrimitives.Plane(40f, 4));
		_cubeMaterial = renderer.CreateMaterial(new PbrMaterial(new Color(0xE0, 0x6A, 0x3B), metallic: 0.1f, roughness: 0.4f));
		_groundMaterial = renderer.CreateMaterial(new PbrMaterial(new Color(0x7A, 0x80, 0x8A), metallic: 0f, roughness: 0.9f));
		renderer.SetEnvironment(new SceneEnvironment { AmbientColor = new Color(0x9C, 0xB0, 0xD0), AmbientIntensity = 0.35f });
		spin.Speed = settings.SpinSpeed;
	}

	/// <summary>Advances the spin (fixed step, so the simulation does not depend on the frame rate).</summary>
	[FixedUpdate]
	public void Spin(GameTime dt)
	{
		spin.Angle = (spin.Angle + spin.Speed * dt.Delta) % MathF.Tau;
		spin.Steps++;
	}

	/// <summary>Submits the camera, the sun, the ground and the cubes.</summary>
	[Render]
	public void Draw(GameTime dt)
	{
		renderer.SetCamera(new Camera { FieldOfView = MathF.PI / 4, Near = 0.5f, Far = 100f, ClearColor = new Color(0x87, 0xA9, 0xD6) },
			Transform.LookAt(new Vector3(0, 6, 12), Vector3.Zero));
		renderer.AddLight(new DirectionalLight(new Color(0xFF, 0xF4, 0xE0), intensity: 3f), Vector3.Normalize(new Vector3(-0.5f, -0.8f, -0.3f)));
		renderer.Draw(_ground, _groundMaterial, Matrix4x4.Identity);

		var cube = new MeshRenderer(_cube, _cubeMaterial);
		for (var i = 0; i < settings.Cubes; i++)
		{
			var x = (i - (settings.Cubes - 1) * 0.5f) * 2f;
			var world = Matrix4x4.CreateRotationY(spin.Angle + i * 0.4f) * Matrix4x4.CreateTranslation(x, 0.5f, 0);
			renderer.Submit(cube, world);
		}
	}
}
