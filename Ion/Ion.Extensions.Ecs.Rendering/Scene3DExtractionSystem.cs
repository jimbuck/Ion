using System.Runtime.CompilerServices;

using Arch.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Ion.Extensions.Graphics;
using Ion.Extensions.Scenes;

namespace Ion.Extensions.Ecs.Rendering;

/// <summary>Options of the <see cref="Scene3DExtractionSystem"/>.</summary>
public sealed class Scene3DExtractionOptions
{
	/// <summary>
	/// Submit only mesh renderers tagged <see cref="Visible"/> (by default every mesh renderer without <see cref="Hidden"/>
	/// is submitted). Cameras and lights are extracted unless <see cref="Hidden"/> either way.
	/// </summary>
	public bool RequireVisible { get; set; }
}

/// <summary>What the last 3D extraction did (the renderer's own culling results are in <c>IRenderer3D.LastFrameStatistics</c>).</summary>
/// <param name="MeshRenderers">Mesh renderers submitted.</param>
/// <param name="Cameras">Cameras added.</param>
/// <param name="Lights">Lights added (directional, point and spot).</param>
/// <param name="EnvironmentUpdates">How many times the world's <see cref="SceneEnvironment"/> was passed to the renderer so far (it is passed when it changes).</param>
public readonly record struct Scene3DExtractionStats(int MeshRenderers, int Cameras, int Lights, int EnvironmentUpdates);

/// <summary>
/// The 3D render extraction (Render, at <see cref="StageOrder.Extract"/>: inside the 3D renderer's scope, after the
/// transform propagation's Render pass), the contract of <c>docs/design/ion-rendering3d.md</c> section 8. Every frame it
/// copies the world into the renderer's submission API (<see cref="IMeshBatch"/>); entities never reach the renderer:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>every <see cref="MeshRenderer"/> with a <see cref="GlobalTransform"/> (without <see cref="Hidden"/>; only with
/// <see cref="Visible"/> under <see cref="Scene3DExtractionOptions.RequireVisible"/>) is submitted at its world matrix
/// (a chunk loop, since the query depends on the options);</item>
/// <item>every <see cref="Camera"/> is added with <c>AddCamera(camera, world)</c> (a camera looks down its entity's -Z);</item>
/// <item>every <see cref="DirectionalLight"/> (shining along the entity's -Z), <see cref="PointLight"/> (at the entity's
/// position) and <see cref="SpotLight"/> (at the entity's position, along its -Z) is added with <c>AddLight</c>;</item>
/// <item>the world's <see cref="SceneEnvironment"/> (see <see cref="Scene3DExtensions.SetEnvironment"/>) is passed with
/// <c>SetEnvironment</c> when it changed since this system last passed it, and the default environment once it is removed.</item>
/// </list>
/// Cameras and lights are <c>[Query]</c> steps (generated chunk loops) that run right after <see cref="Extract"/> at the same
/// order. <see cref="Hidden"/> hides the entity itself, not its children (use <see cref="Scene3DExtensions.SetHidden"/> for a
/// subtree). Allocates nothing per frame.
/// </remarks>
public sealed partial class Scene3DExtractionSystem(World world, IMeshBatch renderer, Scene3DExtractionOptions options)
{
	private static readonly QueryDescription Meshes = new QueryDescription().WithAll<MeshRenderer, GlobalTransform>().WithNone<Hidden>();
	private static readonly QueryDescription VisibleMeshes = new QueryDescription().WithAll<MeshRenderer, GlobalTransform, Visible>().WithNone<Hidden>();

	private SceneEnvironment _environment;
	private bool _hasEnvironment;
	private int _meshes;
	private int _cameras;
	private int _lights;
	private int _environmentUpdates;

	/// <summary>Creates the system with default options.</summary>
	public Scene3DExtractionSystem(World world, IMeshBatch renderer) : this(world, renderer, new Scene3DExtractionOptions())
	{
	}

	/// <summary>The world extracted.</summary>
	public World World => world;

	/// <summary>What the last frame's extraction did (complete once the Render stage's extraction steps ran).</summary>
	public Scene3DExtractionStats LastFrame => new(_meshes, _cameras, _lights, _environmentUpdates);

	/// <summary>Passes the environment when it changed and submits the mesh renderers; the camera and light steps follow.</summary>
	[Render(Order = StageOrder.Extract)]
	public void Extract(GameTime dt)
	{
		_cameras = 0;
		_lights = 0;
		ExtractEnvironment();
		_meshes = ExtractMeshes(options.RequireVisible ? VisibleMeshes : Meshes);
	}

	/// <summary>
	/// The whole extraction outside a schedule (tests, benchmarks, custom loops): <see cref="Extract"/>, then the cameras
	/// and lights. The schedule runs the same steps separately.
	/// </summary>
	public void ExtractAll(GameTime dt)
	{
		Extract(dt);
		__IonQuery_ExtractCamera(dt, world);
		__IonQuery_ExtractDirectionalLight(dt, world);
		__IonQuery_ExtractPointLight(dt, world);
		__IonQuery_ExtractSpotLight(dt, world);
	}

	private int ExtractMeshes(in QueryDescription query)
	{
		var count = 0;
		foreach (ref var chunk in world.Query(in query))
		{
			var n = chunk.Count;
			ref var renderers = ref chunk.GetFirst<MeshRenderer>();
			ref var globals = ref chunk.GetFirst<GlobalTransform>();
			// Arch's query order (last to first within a chunk), as the generated loops.
			for (var i = n - 1; i >= 0; i--)
			{
				renderer.Submit(Unsafe.Add(ref renderers, i), Unsafe.Add(ref globals, i).Matrix);
			}

			count += n;
		}

		return count;
	}

	private void ExtractEnvironment()
	{
		if (world.TryGetEnvironment(out var environment))
		{
			if (_hasEnvironment && Same(environment, _environment)) return;
			renderer.SetEnvironment(environment);
			_environment = environment;
			_hasEnvironment = true;
			_environmentUpdates++;
		}
		else if (_hasEnvironment)
		{
			renderer.SetEnvironment(new SceneEnvironment());
			_hasEnvironment = false;
			_environmentUpdates++;
		}
	}

	private static bool Same(in SceneEnvironment a, in SceneEnvironment b) =>
		a.AmbientColor == b.AmbientColor && a.AmbientIntensity.Equals(b.AmbientIntensity) && a.Skybox == b.Skybox && a.SkyboxIntensity.Equals(b.SkyboxIntensity);

	/// <summary>Adds a camera placed by its entity's world matrix.</summary>
	[Render(Order = StageOrder.Extract), Query, None<Hidden>]
	private void ExtractCamera(in Camera camera, in GlobalTransform transform)
	{
		renderer.AddCamera(camera, transform.Matrix);
		_cameras++;
	}

	/// <summary>Adds a directional light shining along its entity's -Z.</summary>
	[Render(Order = StageOrder.Extract), Query, None<Hidden>]
	private void ExtractDirectionalLight(in DirectionalLight light, in GlobalTransform transform)
	{
		renderer.AddLight(light, transform.Matrix);
		_lights++;
	}

	/// <summary>Adds a point light at its entity's position.</summary>
	[Render(Order = StageOrder.Extract), Query, None<Hidden>]
	private void ExtractPointLight(in PointLight light, in GlobalTransform transform)
	{
		renderer.AddLight(light, transform.Position);
		_lights++;
	}

	/// <summary>Adds a spot light at its entity's position, pointing along its -Z.</summary>
	[Render(Order = StageOrder.Extract), Query, None<Hidden>]
	private void ExtractSpotLight(in SpotLight light, in GlobalTransform transform)
	{
		renderer.AddLight(light, transform.Matrix);
		_lights++;
	}
}

/// <summary>Registration of the 3D render extraction.</summary>
public static class EcsRendering3DBuilderExtensions
{
	/// <summary>
	/// Registers the <see cref="Scene3DExtractionSystem"/> (a transient, so the root schedule and each scene get one bound
	/// to their own world) and its <see cref="Scene3DExtractionOptions"/>. Needs <c>AddEcs()</c> and a 3D renderer
	/// (<c>AddRendering3D()</c>, which provides <see cref="IMeshBatch"/>). Independent of <c>AddEcsRendering()</c> (the 2D
	/// extraction): register both for a scene with sprites and meshes.
	/// </summary>
	public static IServiceCollection AddEcsRendering3D(this IServiceCollection services, Action<Scene3DExtractionOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		var options = new Scene3DExtractionOptions();
		configure?.Invoke(options);
		services.TryAddSingleton(options);
		services.TryAddTransient(static sp => new Scene3DExtractionSystem(sp.GetRequiredService<World>(), sp.GetRequiredService<IMeshBatch>(), sp.GetRequiredService<Scene3DExtractionOptions>()));
		return services;
	}

	/// <summary>Adds the 3D extraction to the root schedule (the root world's meshes, cameras and lights).</summary>
	public static IIonApplication UseEcsRendering3D(this IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);
		return app.UseSystem<Scene3DExtractionSystem>();
	}

	/// <summary>Adds the 3D extraction to a scene's schedule (the scene's world).</summary>
	public static ISceneBuilder UseEcsRendering3D(this ISceneBuilder scene)
	{
		ArgumentNullException.ThrowIfNull(scene);
		return scene.UseSystem<Scene3DExtractionSystem>();
	}
}
