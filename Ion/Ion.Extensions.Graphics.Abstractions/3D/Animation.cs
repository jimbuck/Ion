using System.Numerics;
using System.Runtime.InteropServices;

namespace Ion.Extensions.Graphics;

/// <summary>The four joints that move a skinned vertex: indices into its <see cref="Skeleton"/>'s joints, paired with <see cref="MeshData.Weights"/>.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct JointIndices(ushort J0, ushort J1, ushort J2, ushort J3)
{
	/// <summary>The joint at <paramref name="index"/> (0 to 3).</summary>
	public ushort this[int index] => index switch
	{
		0 => J0,
		1 => J1,
		2 => J2,
		3 => J3,
		_ => throw new ArgumentOutOfRangeException(nameof(index)),
	};
}

/// <summary>A joint (bone) of a <see cref="Skeleton"/>.</summary>
/// <param name="Name">The joint's name (the glTF node's name).</param>
/// <param name="Parent">The index of the parent joint, or -1 for a root joint.</param>
/// <param name="Rest">The local transform when no animation moves the joint (relative to the parent joint, or to <see cref="Skeleton.Root"/>).</param>
/// <param name="InverseBind">The inverse of the joint's model-space matrix at bind time: takes a mesh vertex into the joint's space.</param>
public readonly record struct SkeletonJoint(string Name, int Parent, Transform Rest, Matrix4x4 InverseBind);

/// <summary>
/// The joint hierarchy of a skinned mesh, each joint with its rest transform and inverse bind matrix, in the order the
/// mesh's <see cref="MeshData.Joints"/> index them (parents may come after their children). A pose (one local <see cref="Transform"/> per joint) becomes model-space joint matrices with
/// <see cref="ComputeModelMatrices"/>, and those become the skinning palette the renderer draws with
/// (<see cref="IMeshBatch.Submit(in MeshRenderer, in Matrix4x4, ReadOnlySpan{Matrix4x4})"/>) with
/// <see cref="ComputeSkinningMatrices"/>. <see cref="AnimationPlayer"/> does both every frame.
/// </summary>
public sealed class Skeleton
{
	private readonly SkeletonJoint[] _joints;
	private readonly int[] _order;

	/// <summary>Creates a skeleton.</summary>
	/// <param name="name">A name for logs.</param>
	/// <param name="joints">The joints, in the order vertices index them.</param>
	/// <param name="root">Where the root joints hang in the model (the model matrix of their parent node), identity by default.</param>
	/// <exception cref="ArgumentException">A parent index is out of range, or the parents form a cycle.</exception>
	public Skeleton(string name, IReadOnlyList<SkeletonJoint> joints, Matrix4x4? root = null)
	{
		ArgumentNullException.ThrowIfNull(joints);
		Name = name;
		_joints = [.. joints];
		Root = root ?? Matrix4x4.Identity;
		for (var i = 0; i < _joints.Length; i++)
		{
			var parent = _joints[i].Parent;
			if (parent < -1 || parent >= _joints.Length || parent == i) throw new ArgumentException($"Skeleton '{name}': joint {i} ('{_joints[i].Name}') has parent {parent}, which is not another joint.", nameof(joints));
		}

		// The evaluation order: every parent before its children.
		_order = new int[_joints.Length];
		var state = new byte[_joints.Length];   // 0: not placed, 1: being placed, 2: placed
		var next = 0;
		for (var i = 0; i < _joints.Length; i++) Place(i);

		void Place(int joint)
		{
			if (state[joint] == 2) return;
			if (state[joint] == 1) throw new ArgumentException($"Skeleton '{name}': the parents of joint {joint} ('{_joints[joint].Name}') form a cycle.", nameof(joints));
			state[joint] = 1;
			if (_joints[joint].Parent >= 0) Place(_joints[joint].Parent);
			state[joint] = 2;
			_order[next++] = joint;
		}
	}

	/// <summary>The name.</summary>
	public string Name { get; }

	/// <summary>The joints.</summary>
	public IReadOnlyList<SkeletonJoint> Joints => _joints;

	/// <summary>The number of joints (the length of a pose and of the skinning palette).</summary>
	public int JointCount => _joints.Length;

	/// <summary>The model matrix the root joints are relative to.</summary>
	public Matrix4x4 Root { get; }

	/// <summary>The index of the joint named <paramref name="name"/>, or -1.</summary>
	public int IndexOf(string name)
	{
		for (var i = 0; i < _joints.Length; i++)
		{
			if (_joints[i].Name == name) return i;
		}

		return -1;
	}

	/// <summary>A new pose: every joint at its rest transform.</summary>
	public Transform[] CreateRestPose()
	{
		var pose = new Transform[_joints.Length];
		CopyRestPose(pose);
		return pose;
	}

	/// <summary>Writes the rest transforms into <paramref name="pose"/>.</summary>
	public void CopyRestPose(Span<Transform> pose)
	{
		CheckLength(pose.Length, nameof(pose));
		for (var i = 0; i < _joints.Length; i++) pose[i] = _joints[i].Rest;
	}

	/// <summary>The model-space matrix of every joint of <paramref name="pose"/> (each local transform applied under its parent's).</summary>
	public void ComputeModelMatrices(ReadOnlySpan<Transform> pose, Span<Matrix4x4> model)
	{
		CheckLength(pose.Length, nameof(pose));
		CheckLength(model.Length, nameof(model));
		foreach (var i in _order)
		{
			var parent = _joints[i].Parent;
			model[i] = pose[i].ToMatrix() * (parent < 0 ? Root : model[parent]);
		}
	}

	/// <summary>
	/// The skinning palette of model-space joint matrices: each joint's inverse bind matrix times its model matrix, so a
	/// vertex at its bind position follows the joint. The renderer blends the four of each vertex by its weights.
	/// </summary>
	public void ComputeSkinningMatrices(ReadOnlySpan<Matrix4x4> model, Span<Matrix4x4> skinning)
	{
		CheckLength(model.Length, nameof(model));
		CheckLength(skinning.Length, nameof(skinning));
		for (var i = 0; i < _joints.Length; i++) skinning[i] = _joints[i].InverseBind * model[i];
	}

	private void CheckLength(int length, string name)
	{
		if (length < _joints.Length) throw new ArgumentException($"Skeleton '{Name}' has {_joints.Length} joints; the span has {length}.", name);
	}
}

/// <summary>The property of a joint an <see cref="AnimationChannel"/> animates.</summary>
public enum AnimationPath
{
	/// <summary><see cref="Transform.Position"/> (xyz of the values).</summary>
	Translation,
	/// <summary><see cref="Transform.Rotation"/> (a quaternion, xyzw).</summary>
	Rotation,
	/// <summary><see cref="Transform.Scale"/> (xyz of the values).</summary>
	Scale,
}

/// <summary>How an <see cref="AnimationChannel"/> goes from one key to the next (glTF's interpolation modes).</summary>
public enum AnimationInterpolation
{
	/// <summary>Each key holds until the next.</summary>
	Step,
	/// <summary>Straight lines between keys (rotations: spherical).</summary>
	Linear,
	/// <summary>A cubic Hermite spline: three values per key (in-tangent, value, out-tangent).</summary>
	CubicSpline,
}

/// <summary>The keys of one property of one joint over time.</summary>
public sealed class AnimationChannel
{
	/// <summary>Creates a channel.</summary>
	/// <param name="joint">The joint animated (an index into the skeleton's joints).</param>
	/// <param name="path">The property animated.</param>
	/// <param name="interpolation">How keys are joined.</param>
	/// <param name="times">The key times in seconds, ascending.</param>
	/// <param name="values">One value per key (three for <see cref="AnimationInterpolation.CubicSpline"/>: in-tangent, value, out-tangent).</param>
	/// <exception cref="ArgumentException">The times are not ascending, or the value count does not match.</exception>
	public AnimationChannel(int joint, AnimationPath path, AnimationInterpolation interpolation, float[] times, Vector4[] values)
	{
		ArgumentNullException.ThrowIfNull(times);
		ArgumentNullException.ThrowIfNull(values);
		ArgumentOutOfRangeException.ThrowIfNegative(joint);
		if (times.Length == 0) throw new ArgumentException("A channel needs at least one key.", nameof(times));
		for (var i = 1; i < times.Length; i++)
		{
			if (times[i] < times[i - 1]) throw new ArgumentException($"Key {i} at {times[i]} s comes before key {i - 1} at {times[i - 1]} s.", nameof(times));
		}

		var perKey = interpolation == AnimationInterpolation.CubicSpline ? 3 : 1;
		if (values.Length != times.Length * perKey) throw new ArgumentException($"{times.Length} keys need {times.Length * perKey} values for {interpolation}; there are {values.Length}.", nameof(values));
		Joint = joint;
		Path = path;
		Interpolation = interpolation;
		Times = times;
		Values = values;
	}

	/// <summary>The joint animated.</summary>
	public int Joint { get; }

	/// <summary>The property animated.</summary>
	public AnimationPath Path { get; }

	/// <summary>How keys are joined.</summary>
	public AnimationInterpolation Interpolation { get; }

	/// <summary>The key times in seconds.</summary>
	public float[] Times { get; }

	/// <summary>The key values (see the constructor).</summary>
	public Vector4[] Values { get; }

	/// <summary>The time of the last key.</summary>
	public float Duration => Times[^1];

	/// <summary>The value at <paramref name="time"/> (held before the first key and after the last; rotations normalized).</summary>
	public Vector4 Sample(float time)
	{
		var times = Times;
		var cubic = Interpolation == AnimationInterpolation.CubicSpline;
		if (time <= times[0] || times.Length == 1) return Value(0);
		if (time >= times[^1]) return Value(times.Length - 1);

		// The last key at or before the time.
		var index = Array.BinarySearch(times, time);
		var k = index >= 0 ? index : ~index - 1;
		if (index >= 0) return Value(k);
		if (Interpolation == AnimationInterpolation.Step) return Value(k);

		var t0 = times[k];
		var t1 = times[k + 1];
		var dt = t1 - t0;
		var s = dt > 0 ? (time - t0) / dt : 0f;
		if (!cubic)
		{
			if (Path == AnimationPath.Rotation) return ToVector(Quaternion.Slerp(ToQuaternion(Normalize(Values[k])), ToQuaternion(Normalize(Values[k + 1])), s));
			return Vector4.Lerp(Values[k], Values[k + 1], s);
		}

		// glTF's Hermite spline: p0 and its out-tangent, p1 and its in-tangent (tangents scaled by the key interval).
		var p0 = Values[k * 3 + 1];
		var m0 = Values[k * 3 + 2] * dt;
		var p1 = Values[(k + 1) * 3 + 1];
		var m1 = Values[(k + 1) * 3] * dt;
		var s2 = s * s;
		var s3 = s2 * s;
		var result = (2 * s3 - 3 * s2 + 1) * p0 + (s3 - 2 * s2 + s) * m0 + (-2 * s3 + 3 * s2) * p1 + (s3 - s2) * m1;
		return Path == AnimationPath.Rotation ? Normalize(result) : result;

		Vector4 Value(int key)
		{
			var v = Values[cubic ? key * 3 + 1 : key];
			return Path == AnimationPath.Rotation ? Normalize(v) : v;
		}
	}

	/// <summary>Writes the value at <paramref name="time"/> into its property of <paramref name="transform"/>.</summary>
	public void Apply(float time, ref Transform transform)
	{
		var value = Sample(time);
		switch (Path)
		{
			case AnimationPath.Translation: transform.Position = new Vector3(value.X, value.Y, value.Z); break;
			case AnimationPath.Rotation: transform.Rotation = ToQuaternion(value); break;
			default: transform.Scale = new Vector3(value.X, value.Y, value.Z); break;
		}
	}

	private static Quaternion ToQuaternion(Vector4 v) => new(v.X, v.Y, v.Z, v.W);

	private static Vector4 ToVector(Quaternion q) => new(q.X, q.Y, q.Z, q.W);

	private static Vector4 Normalize(Vector4 v) => v.LengthSquared() > 1e-12f ? Vector4.Normalize(v) : new Vector4(0, 0, 0, 1);
}

/// <summary>
/// An animation of a skeleton: channels of joint translations, rotations and scales over time, loaded from glTF
/// (<see cref="IModel.Animations"/>) or built by hand. <see cref="Sample"/> writes a pose; <see cref="AnimationPlayer"/>
/// plays clips and blends between them.
/// </summary>
public sealed class AnimationClip
{
	private readonly AnimationChannel[] _channels;

	/// <summary>Creates a clip; its duration is the last key time of its channels unless given.</summary>
	public AnimationClip(string name, IReadOnlyList<AnimationChannel> channels, float? duration = null)
	{
		ArgumentNullException.ThrowIfNull(channels);
		Name = name;
		_channels = [.. channels];
		var last = 0f;
		foreach (var channel in _channels) last = MathF.Max(last, channel.Duration);
		Duration = duration ?? last;
	}

	/// <summary>The name.</summary>
	public string Name { get; }

	/// <summary>The channels.</summary>
	public IReadOnlyList<AnimationChannel> Channels => _channels;

	/// <summary>The length in seconds.</summary>
	public float Duration { get; }

	/// <summary>The highest joint index the clip animates, plus one (a skeleton needs at least this many joints to play it).</summary>
	public int JointSpan
	{
		get
		{
			var span = 0;
			foreach (var channel in _channels) span = Math.Max(span, channel.Joint + 1);
			return span;
		}
	}

	/// <summary>
	/// Writes the clip at <paramref name="time"/> (seconds, clamped to the clip) into <paramref name="pose"/>. Joints and
	/// properties the clip does not animate keep their values, so start from the rest pose
	/// (<see cref="Skeleton.CopyRestPose"/>). Channels of joints beyond the pose are skipped.
	/// </summary>
	public void Sample(float time, Span<Transform> pose)
	{
		time = Math.Clamp(time, 0f, Duration);
		foreach (var channel in _channels)
		{
			if ((uint)channel.Joint < (uint)pose.Length) channel.Apply(time, ref pose[channel.Joint]);
		}
	}
}

/// <summary>Pose arithmetic.</summary>
public static class AnimationPose
{
	/// <summary>The transform <paramref name="weight"/> of the way from <paramref name="a"/> to <paramref name="b"/> (positions and scales linear, rotations spherical).</summary>
	public static Transform Blend(in Transform a, in Transform b, float weight) =>
		new(Vector3.Lerp(a.Position, b.Position, weight), Quaternion.Slerp(a.Rotation, b.Rotation, weight), Vector3.Lerp(a.Scale, b.Scale, weight));

	/// <summary>Blends two poses joint by joint into <paramref name="result"/> (which may be either of them).</summary>
	public static void Blend(ReadOnlySpan<Transform> a, ReadOnlySpan<Transform> b, float weight, Span<Transform> result)
	{
		var n = Math.Min(result.Length, Math.Min(a.Length, b.Length));
		for (var i = 0; i < n; i++) result[i] = Blend(a[i], b[i], weight);
	}
}

/// <summary>
/// Plays <see cref="AnimationClip"/>s on a <see cref="Skeleton"/>: advances time, loops or holds, cross-fades from one
/// clip to the next, and every <see cref="Update"/> works out the pose, the model-space joint matrices (to attach things
/// to a hand, say) and the skinning palette the renderer draws with. Allocation-free once created.
/// </summary>
/// <example>
/// <code>
/// var player = new AnimationPlayer(model.Skins[0]);
/// player.Play(model.Animations[0]);
/// // every frame:
/// player.Update(dt);
/// renderer.Submit(meshRenderer, world, player.SkinningMatrices);
/// </code>
/// </example>
public sealed class AnimationPlayer
{
	private readonly Transform[] _pose;
	private readonly Transform[] _scratch;
	private readonly Matrix4x4[] _model;
	private readonly Matrix4x4[] _skinning;

	/// <summary>Creates a player at the skeleton's rest pose.</summary>
	public AnimationPlayer(Skeleton skeleton)
	{
		ArgumentNullException.ThrowIfNull(skeleton);
		Skeleton = skeleton;
		_pose = skeleton.CreateRestPose();
		_scratch = new Transform[skeleton.JointCount];
		_model = new Matrix4x4[skeleton.JointCount];
		_skinning = new Matrix4x4[skeleton.JointCount];
		Evaluate();
	}

	/// <summary>The skeleton.</summary>
	public Skeleton Skeleton { get; }

	/// <summary>The clip playing (null: the rest pose).</summary>
	public AnimationClip? Clip { get; private set; }

	/// <summary>The time in <see cref="Clip"/>, in seconds.</summary>
	public float Time { get; set; }

	/// <summary>The playback rate of <see cref="Clip"/> (1: normal, negative: backwards).</summary>
	public float Speed { get; set; } = 1f;

	/// <summary>Whether <see cref="Clip"/> starts over at its end (otherwise it holds its last pose).</summary>
	public bool Loop { get; set; } = true;

	/// <summary>The clip being faded in by <see cref="CrossFade"/>, or null.</summary>
	public AnimationClip? Next { get; private set; }

	/// <summary>The time in <see cref="Next"/>.</summary>
	public float NextTime { get; private set; }

	/// <summary>How far the cross-fade is, from 0 (all <see cref="Clip"/>) to 1 (all <see cref="Next"/>).</summary>
	public float FadeWeight => Next is null || _fadeDuration <= 0 ? 0f : Math.Clamp(_fadeElapsed / _fadeDuration, 0f, 1f);

	/// <summary>Whether a non-looping clip has reached its end.</summary>
	public bool IsFinished => Clip is { } clip && !Loop && (Speed >= 0 ? Time >= clip.Duration : Time <= 0);

	/// <summary>The pose of the last evaluation: one local transform per joint.</summary>
	public ReadOnlySpan<Transform> Pose => _pose;

	/// <summary>The model-space matrix of every joint (multiply by the model's world matrix for world space).</summary>
	public ReadOnlySpan<Matrix4x4> JointMatrices => _model;

	/// <summary>The skinning palette to submit with the skinned mesh renderers (one matrix per joint).</summary>
	public ReadOnlySpan<Matrix4x4> SkinningMatrices => _skinning;

	private bool _nextLoop;
	private float _nextSpeed;
	private float _fadeDuration;
	private float _fadeElapsed;

	/// <summary>Plays <paramref name="clip"/> from the start, at once (cancelling a cross-fade).</summary>
	/// <exception cref="ArgumentException">The clip animates joints the skeleton does not have.</exception>
	public void Play(AnimationClip clip, bool loop = true, float speed = 1f)
	{
		Check(clip);
		Clip = clip;
		Time = speed < 0 ? clip.Duration : 0f;
		Loop = loop;
		Speed = speed;
		Next = null;
		Evaluate();
	}

	/// <summary>
	/// Fades from the playing clip to <paramref name="clip"/> (from its start) over <paramref name="duration"/> seconds,
	/// both playing meanwhile; then <paramref name="clip"/> is <see cref="Clip"/>. Without a playing clip, or with a
	/// duration of 0, it plays at once.
	/// </summary>
	public void CrossFade(AnimationClip clip, float duration, bool loop = true, float speed = 1f)
	{
		Check(clip);
		if (Clip is null || duration <= 0)
		{
			Play(clip, loop, speed);
			return;
		}

		Next = clip;
		NextTime = speed < 0 ? clip.Duration : 0f;
		_nextLoop = loop;
		_nextSpeed = speed;
		_fadeDuration = duration;
		_fadeElapsed = 0f;
	}

	/// <summary>Stops playing and goes back to the rest pose.</summary>
	public void Stop()
	{
		Clip = null;
		Next = null;
		Time = 0;
		Evaluate();
	}

	/// <summary>Advances the clips by <paramref name="deltaTime"/> seconds and evaluates the pose and matrices.</summary>
	public void Update(float deltaTime)
	{
		if (Clip is { } clip) Time = Advance(clip, Time, deltaTime * Speed, Loop);
		if (Next is { } next)
		{
			NextTime = Advance(next, NextTime, deltaTime * _nextSpeed, _nextLoop);
			_fadeElapsed += deltaTime;
			if (_fadeElapsed >= _fadeDuration)
			{
				Clip = next;
				Time = NextTime;
				Loop = _nextLoop;
				Speed = _nextSpeed;
				Next = null;
			}
		}

		Evaluate();
	}

	/// <summary>Works out the pose and the matrices from the clips and times as they are (after setting <see cref="Time"/>, say).</summary>
	public void Evaluate()
	{
		Skeleton.CopyRestPose(_pose);
		Clip?.Sample(Time, _pose);
		if (Next is { } next)
		{
			Skeleton.CopyRestPose(_scratch);
			next.Sample(NextTime, _scratch);
			AnimationPose.Blend(_pose, _scratch, FadeWeight, _pose);
		}

		Skeleton.ComputeModelMatrices(_pose, _model);
		Skeleton.ComputeSkinningMatrices(_model, _skinning);
	}

	private static float Advance(AnimationClip clip, float time, float delta, bool loop)
	{
		time += delta;
		var duration = clip.Duration;
		if (duration <= 0) return 0f;
		if (loop)
		{
			time %= duration;
			if (time < 0) time += duration;
			return time;
		}

		return Math.Clamp(time, 0f, duration);
	}

	private void Check(AnimationClip clip)
	{
		ArgumentNullException.ThrowIfNull(clip);
		if (clip.JointSpan > Skeleton.JointCount) throw new ArgumentException($"Clip '{clip.Name}' animates joint {clip.JointSpan - 1}; skeleton '{Skeleton.Name}' has {Skeleton.JointCount} joints.", nameof(clip));
	}
}
