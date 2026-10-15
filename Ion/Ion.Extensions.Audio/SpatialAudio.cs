using System.Numerics;

namespace Ion.Extensions.Audio;

/// <summary>
/// The engine's <see cref="ISpatialAudio"/>: keeps each positional voice's position, volume and falloff, and in
/// <see cref="Update"/> (called by the audio system just before the frame's commands are flushed) sends every voice whose
/// gain or pan changed a volume and pan command.
/// </summary>
public sealed class SpatialAudio(IAudioManager audio) : ISpatialAudio
{
	private readonly Dictionary<int, Emitter> _emitters = [];
	private readonly List<int> _ended = [];
	private readonly List<(int Slot, Emitter Emitter)> _changed = [];
	private Vector3 _right = Vector3.UnitX;

	/// <inheritdoc/>
	public Vector3 ListenerPosition { get; private set; }

	/// <inheritdoc/>
	public Vector3 ListenerForward { get; private set; } = -Vector3.UnitZ;

	/// <inheritdoc/>
	public Vector3 ListenerUp { get; private set; } = Vector3.UnitY;

	/// <inheritdoc/>
	public float PanStrength { get; set; } = 1f;

	/// <summary>The positional voices still tracked.</summary>
	public int Count => _emitters.Count;

	/// <inheritdoc/>
	public void SetListener(Vector3 position, Vector3 forward, Vector3 up)
	{
		ListenerPosition = Finite(position) ? position : ListenerPosition;
		if (forward.LengthSquared() > 1e-12f && up.LengthSquared() > 1e-12f && Finite(forward) && Finite(up))
		{
			var right = Vector3.Cross(forward, up);
			if (right.LengthSquared() > 1e-12f)
			{
				ListenerForward = Vector3.Normalize(forward);
				ListenerUp = Vector3.Normalize(up);
				_right = Vector3.Normalize(right);
			}
		}
	}

	/// <inheritdoc/>
	public void SetListener2D(Vector2 position) =>
		// Facing the screen (into +z) with up towards -y: screen right is +x.
		SetListener(new Vector3(position, 0), Vector3.UnitZ, -Vector3.UnitY);

	/// <inheritdoc/>
	public VoiceHandle Play(ISoundEffect sound, Vector3 position, float volume = 1f, float pitchShift = 0f, bool loop = false, AudioBus bus = AudioBus.Sfx, Attenuation? attenuation = null, float fadeIn = 0f)
	{
		ArgumentNullException.ThrowIfNull(sound);
		var falloff = attenuation ?? Attenuation.Default;
		volume = float.IsFinite(volume) ? Math.Max(0f, volume) : 0f;
		var (gain, pan) = Evaluate(position, falloff);
		var voice = audio.Play(sound, volume * gain, pitchShift, pan, loop, bus, fadeIn);
		if (voice.IsValid) _emitters[voice.Slot] = new Emitter(voice.Generation, position, volume, falloff, gain, pan);
		return voice;
	}

	/// <inheritdoc/>
	public VoiceHandle Play2D(ISoundEffect sound, Vector2 position, float volume = 1f, float pitchShift = 0f, bool loop = false, AudioBus bus = AudioBus.Sfx, Attenuation? attenuation = null, float fadeIn = 0f) =>
		Play(sound, new Vector3(position, 0), volume, pitchShift, loop, bus, attenuation ?? Attenuation.Pixels, fadeIn);

	/// <inheritdoc/>
	public void SetPosition(VoiceHandle voice, Vector3 position)
	{
		if (!Finite(position) || !_emitters.TryGetValue(voice.Slot, out var emitter) || emitter.Generation != voice.Generation) return;
		_emitters[voice.Slot] = emitter with { Position = position };
	}

	/// <inheritdoc/>
	public void SetPosition2D(VoiceHandle voice, Vector2 position) => SetPosition(voice, new Vector3(position, 0));

	/// <inheritdoc/>
	public void SetVolume(VoiceHandle voice, float volume)
	{
		if (!_emitters.TryGetValue(voice.Slot, out var emitter) || emitter.Generation != voice.Generation) return;
		_emitters[voice.Slot] = emitter with { Volume = float.IsFinite(volume) ? Math.Max(0f, volume) : 0f, Gain = -1f };
	}

	/// <inheritdoc/>
	public bool IsPositional(VoiceHandle voice) =>
		_emitters.TryGetValue(voice.Slot, out var emitter) && emitter.Generation == voice.Generation && audio.IsPlaying(voice);

	/// <inheritdoc/>
	public (float Gain, float Pan) Evaluate(Vector3 position, Attenuation attenuation)
	{
		var offset = position - ListenerPosition;
		var distance = offset.Length();
		var gain = attenuation.GainAt(distance);

		// The left-right part of the direction; within the minimum distance it narrows to the center, so a sound passing
		// through the listener does not flip from one ear to the other.
		var spread = Math.Max(distance, attenuation.EffectiveMinDistance);
		var pan = distance > 0 ? Vector3.Dot(offset, _right) / spread : 0f;
		pan = Math.Clamp(pan * Math.Clamp(PanStrength, 0f, 1f), -1f, 1f);
		return (gain, float.IsFinite(pan) ? pan : 0f);
	}

	/// <summary>
	/// Sends the volume and pan of every positional voice whose gain or pan changed since the last update, and forgets
	/// the voices that ended. Called once per frame by the audio system, before the commands are flushed.
	/// </summary>
	public void Update()
	{
		if (_emitters.Count == 0) return;
		_ended.Clear();
		_changed.Clear();
		foreach (var (slot, emitter) in _emitters)
		{
			var voice = new VoiceHandle(slot, emitter.Generation);
			if (!audio.IsPlaying(voice))
			{
				_ended.Add(slot);
				continue;
			}

			var (gain, pan) = Evaluate(emitter.Position, emitter.Attenuation);
			if (gain == emitter.Gain && pan == emitter.Pan) continue;
			if (gain != emitter.Gain) audio.SetVolume(voice, emitter.Volume * gain);
			if (pan != emitter.Pan) audio.SetPan(voice, pan);
			_changed.Add((slot, emitter with { Gain = gain, Pan = pan }));
		}

		foreach (var (slot, emitter) in _changed) _emitters[slot] = emitter;
		foreach (var slot in _ended) _emitters.Remove(slot);
	}

	private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

	private readonly record struct Emitter(int Generation, Vector3 Position, float Volume, Attenuation Attenuation, float Gain, float Pan);
}
