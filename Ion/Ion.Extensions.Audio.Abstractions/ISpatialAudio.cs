using System.Numerics;

namespace Ion.Extensions.Audio;

/// <summary>How a positional voice's gain falls off with its distance from the listener.</summary>
public enum DistanceModel
{
	/// <summary>No falloff: only the pan changes with direction.</summary>
	None = 0,

	/// <summary>
	/// <c>min / (min + rolloff * (d - min))</c> with the distance clamped to [min, max]: full gain within
	/// <see cref="Attenuation.MinDistance"/>, half at twice that (with a rolloff of 1), constant beyond the maximum. The
	/// natural falloff of a point source (the OpenAL default).
	/// </summary>
	Inverse = 1,

	/// <summary>From full gain at <see cref="Attenuation.MinDistance"/> down to silence at <see cref="Attenuation.MaxDistance"/> (scaled by the rolloff): sounds have a hard edge.</summary>
	Linear = 2,

	/// <summary><c>(d / min) ^ -rolloff</c> with the distance clamped to [min, max].</summary>
	Exponential = 3,
}

/// <summary>
/// The distance falloff of a positional voice, in world units (pixels in most 2D games, meters in most 3D ones).
/// </summary>
/// <remarks><c>default(Attenuation)</c> never falls off (<see cref="DistanceModel.None"/>, minimum distance 1); use <see cref="Default"/> or the constructor.</remarks>
/// <param name="MinDistance">Within this distance the voice plays at full gain, and its pan narrows to the center as it gets closer (positive).</param>
/// <param name="MaxDistance">Beyond this distance the gain no longer falls (<see cref="DistanceModel.Linear"/>: it is silent).</param>
/// <param name="Rolloff">How fast the gain falls (1 is the model's natural rate; 0 never falls).</param>
/// <param name="Model">The falloff curve.</param>
public readonly record struct Attenuation(float MinDistance = 1f, float MaxDistance = 100f, float Rolloff = 1f, DistanceModel Model = DistanceModel.Inverse)
{
	/// <summary>The default: inverse distance from 1 unit, up to 100.</summary>
	public static Attenuation Default => new(1f, 100f, 1f, DistanceModel.Inverse);

	/// <summary>Inverse falloff scaled for pixel units: full gain within 64 pixels, constant beyond 4096.</summary>
	public static Attenuation Pixels => new(64f, 4096f, 1f, DistanceModel.Inverse);

	/// <summary>The minimum distance in use (1 when not positive).</summary>
	public float EffectiveMinDistance => MinDistance > 0 && float.IsFinite(MinDistance) ? MinDistance : 1f;

	/// <summary>The gain at <paramref name="distance"/> from the listener, in [0, 1].</summary>
	public float GainAt(float distance)
	{
		var min = EffectiveMinDistance;
		var max = MaxDistance > min ? MaxDistance : min;
		var rolloff = Rolloff > 0 && float.IsFinite(Rolloff) ? Rolloff : 0f;
		if (!float.IsFinite(distance)) distance = max;
		var d = Math.Clamp(distance, min, max);
		var gain = Model switch
		{
			DistanceModel.None => 1f,
			DistanceModel.Linear => 1f - rolloff * (d - min) / Math.Max(max - min, 1e-6f),
			DistanceModel.Exponential => MathF.Pow(d / min, -rolloff),
			_ => min / (min + rolloff * (d - min)),
		};
		return Math.Clamp(gain, 0f, 1f);
	}
}

/// <summary>
/// Positional audio on top of <see cref="IAudioManager"/>: a listener and voices with positions, whose gain falls off with
/// distance (<see cref="Attenuation"/>) and whose pan follows their direction from the listener. Work in 3D
/// (<see cref="SetListener"/>, <see cref="Play"/>) or 2D (<see cref="SetListener2D"/>, <see cref="Play2D"/>, screen
/// coordinates with y down); a 2D position is the 3D point (x, y, 0).
/// </summary>
/// <remarks>
/// Gains and pans are worked out on the game thread once per frame, in the audio flush (Last, <c>StageOrder.Audio</c>),
/// from the positions set by then, and sent as ordinary volume and pan changes (ramped over one buffer), so positional
/// voices mix like any other and stay deterministic. Stereo only: the pan is the left-right component of the direction,
/// so a sound straight ahead and one straight behind sound alike. Stop, pitch and seek a positional voice through
/// <see cref="IAudioManager"/>; set its volume here, since the falloff scales it.
/// </remarks>
public interface ISpatialAudio
{
	/// <summary>The listener's position.</summary>
	Vector3 ListenerPosition { get; }

	/// <summary>The direction the listener faces.</summary>
	Vector3 ListenerForward { get; }

	/// <summary>The listener's up direction.</summary>
	Vector3 ListenerUp { get; }

	/// <summary>How much positional voices are panned, from 0 (centered) to 1 (fully left or right when at the side). Defaults to 1.</summary>
	float PanStrength { get; set; }

	/// <summary>Places the listener in 3D (usually at the camera): its position, the direction it faces and its up.</summary>
	void SetListener(Vector3 position, Vector3 forward, Vector3 up);

	/// <summary>Places the listener in 2D screen space (y down; usually the camera's center), facing the screen.</summary>
	void SetListener2D(Vector2 position);

	/// <summary>
	/// Starts <paramref name="sound"/> as a positional voice at <paramref name="position"/>, with its gain and pan for
	/// where it is (see <see cref="IAudioManager.Play"/> for the other parameters).
	/// </summary>
	VoiceHandle Play(ISoundEffect sound, Vector3 position, float volume = 1f, float pitchShift = 0f, bool loop = false, AudioBus bus = AudioBus.Sfx, Attenuation? attenuation = null, float fadeIn = 0f);

	/// <summary>Starts a positional voice at a 2D position (see <see cref="Play"/>). The attenuation defaults to <see cref="Attenuation.Pixels"/>.</summary>
	VoiceHandle Play2D(ISoundEffect sound, Vector2 position, float volume = 1f, float pitchShift = 0f, bool loop = false, AudioBus bus = AudioBus.Sfx, Attenuation? attenuation = null, float fadeIn = 0f);

	/// <summary>Moves a positional voice (for example an engine on a moving car). Does nothing for other voices.</summary>
	void SetPosition(VoiceHandle voice, Vector3 position);

	/// <summary>Moves a positional voice in 2D.</summary>
	void SetPosition2D(VoiceHandle voice, Vector2 position);

	/// <summary>Changes a positional voice's volume before the falloff.</summary>
	void SetVolume(VoiceHandle voice, float volume);

	/// <summary>Whether <paramref name="voice"/> is a positional voice that is still playing.</summary>
	bool IsPositional(VoiceHandle voice);

	/// <summary>The gain and pan a voice at <paramref name="position"/> gets from the listener as it is now.</summary>
	(float Gain, float Pan) Evaluate(Vector3 position, Attenuation attenuation);
}

/// <summary>
/// ECS: the entity the spatial listener follows (usually the camera), with the ECS module's
/// <c>UseEcsAudio()</c>: its world position and, in 3D, the direction it faces (-Z) and its up (+Y). The first one found
/// is used.
/// </summary>
public readonly record struct AudioListener;

/// <summary>
/// ECS: a positional voice that follows its entity, with the ECS module's <c>UseEcsAudio()</c>: start it with
/// <see cref="ISpatialAudio.Play"/> (or <see cref="ISpatialAudio.Play2D"/>) and add this to the entity, and the voice
/// moves with the entity's world position every frame.
/// </summary>
/// <param name="Voice">The positional voice.</param>
public readonly record struct AudioEmitter(VoiceHandle Voice);
