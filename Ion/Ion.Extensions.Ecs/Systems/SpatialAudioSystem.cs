using System.Numerics;

using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Audio;
using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs;

/// <summary>
/// Positional audio from the ECS (<see cref="EcsBuilderExtensions.UseEcsAudio(IIonApplication)"/>): places the
/// <see cref="ISpatialAudio"/> listener at the world transform of the entity with an <see cref="AudioListener"/>, and
/// moves every <see cref="AudioEmitter"/>'s voice to its entity's world position, in Last just before the audio flush
/// (<see cref="StageOrder.Audio"/> - 10), with the world transforms the Render propagation computed. 3D entities
/// (<see cref="GlobalTransform"/>) win over 2D ones (<see cref="GlobalTransform2D"/>). Does nothing without the audio module.
/// </summary>
public sealed class SpatialAudioSystem(World world, IServiceProvider services)
{
	private static readonly QueryDescription Listeners3D = new QueryDescription().WithAll<AudioListener, GlobalTransform>();
	private static readonly QueryDescription Listeners2D = new QueryDescription().WithAll<AudioListener, GlobalTransform2D>();
	private static readonly QueryDescription Emitters3D = new QueryDescription().WithAll<AudioEmitter, GlobalTransform>();
	private static readonly QueryDescription Emitters2D = new QueryDescription().WithAll<AudioEmitter, GlobalTransform2D>().WithNone<GlobalTransform>();

	private readonly ISpatialAudio? _spatial = services.GetService<ISpatialAudio>();

	static SpatialAudioSystem()
	{
		EcsComponents.Register<AudioListener>();
		EcsComponents.Register<AudioEmitter>();
	}

	/// <summary>Places the listener and the emitters.</summary>
	[Last(Order = StageOrder.Audio - 10)]
	public void Sync(GameTime dt)
	{
		if (_spatial is not { } spatial) return;

		var placed = false;
		foreach (ref var chunk in world.Query(in Listeners3D))
		{
			ref readonly var global = ref chunk.GetFirst<GlobalTransform>();
			var matrix = global.Matrix;
			spatial.SetListener(matrix.Translation, Vector3.TransformNormal(-Vector3.UnitZ, matrix), Vector3.TransformNormal(Vector3.UnitY, matrix));
			placed = true;
			break;
		}

		if (!placed)
		{
			foreach (ref var chunk in world.Query(in Listeners2D))
			{
				spatial.SetListener2D(chunk.GetFirst<GlobalTransform2D>().Position);
				break;
			}
		}

		foreach (ref var chunk in world.Query(in Emitters3D))
		{
			var emitters = chunk.GetSpan<AudioEmitter>();
			var globals = chunk.GetSpan<GlobalTransform>();
			for (var i = 0; i < chunk.Count; i++) spatial.SetPosition(emitters[i].Voice, globals[i].Matrix.Translation);
		}

		foreach (ref var chunk in world.Query(in Emitters2D))
		{
			var emitters = chunk.GetSpan<AudioEmitter>();
			var globals = chunk.GetSpan<GlobalTransform2D>();
			for (var i = 0; i < chunk.Count; i++) spatial.SetPosition2D(emitters[i].Voice, globals[i].Position);
		}
	}
}
