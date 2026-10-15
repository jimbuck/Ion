using System.Numerics;

namespace Ion.Extensions.Graphics.Abstractions.Tests;

/// <summary>Pose evaluation: channel interpolation, clips, skeletons, skinning palettes, blending and the player.</summary>
public class AnimationTests
{
	private static void Near(Vector3 expected, Vector3 actual, float epsilon = 1e-4f) =>
		Assert.True(Vector3.Distance(expected, actual) < epsilon, $"Expected {expected}, got {actual}.");

	private static void Near(Quaternion expected, Quaternion actual, float epsilon = 1e-4f) =>
		Assert.True(MathF.Abs(MathF.Abs(Quaternion.Dot(expected, actual)) - 1) < epsilon, $"Expected {expected}, got {actual}.");

	private static Vector4 V(float x, float y = 0, float z = 0) => new(x, y, z, 0);

	private static Vector4 Q(Quaternion q) => new(q.X, q.Y, q.Z, q.W);

	private static AnimationChannel Move(int joint, float[] times, params float[] xs) =>
		new(joint, AnimationPath.Translation, AnimationInterpolation.Linear, times, [.. xs.Select(x => V(x))]);

	/// <summary>A chain of joints along +X, one unit apart, the first at the origin; bound at rest.</summary>
	private static Skeleton Chain(int joints)
	{
		var list = new SkeletonJoint[joints];
		for (var i = 0; i < joints; i++)
		{
			var rest = new Transform(i == 0 ? Vector3.Zero : Vector3.UnitX);
			list[i] = new SkeletonJoint($"j{i}", i - 1, rest, Matrix4x4.CreateTranslation(-i, 0, 0));
		}

		return new Skeleton("chain", list);
	}

	[Fact]
	public void LinearChannelsInterpolateAndHoldOutsideTheirKeys()
	{
		var channel = Move(0, [1f, 3f], 10f, 20f);
		Assert.Equal(10f, channel.Sample(0f).X);
		Assert.Equal(10f, channel.Sample(1f).X);
		Assert.Equal(15f, channel.Sample(2f).X, 4);
		Assert.Equal(17.5f, channel.Sample(2.5f).X, 4);
		Assert.Equal(20f, channel.Sample(3f).X);
		Assert.Equal(20f, channel.Sample(9f).X);
		Assert.Equal(3f, channel.Duration);
	}

	[Fact]
	public void StepChannelsHoldEachKeyUntilTheNext()
	{
		var channel = new AnimationChannel(0, AnimationPath.Scale, AnimationInterpolation.Step, [0f, 1f, 2f], [V(1, 1, 1), V(2, 2, 2), V(3, 3, 3)]);
		Assert.Equal(1f, channel.Sample(0.99f).X);
		Assert.Equal(2f, channel.Sample(1f).X);
		Assert.Equal(2f, channel.Sample(1.5f).X);
		Assert.Equal(3f, channel.Sample(2.5f).X);
	}

	[Fact]
	public void RotationsInterpolateSphericallyAndComeOutNormalized()
	{
		var a = Quaternion.Identity;
		var b = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
		var channel = new AnimationChannel(0, AnimationPath.Rotation, AnimationInterpolation.Linear, [0f, 1f], [Q(a), Q(b) * 3]);   // a key that is not unit length
		var half = channel.Sample(0.5f);
		Assert.Equal(1f, half.Length(), 4);
		Near(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4), new Quaternion(half.X, half.Y, half.Z, half.W));
		Assert.Equal(1f, channel.Sample(1f).Length(), 4);
	}

	[Fact]
	public void CubicSplinesFollowGltfsHermiteFormula()
	{
		// Keys 0 and 1 at value 0, key 0's out-tangent 1, every other tangent 0: p(s) = (s^3 - 2s^2 + s) * dt * 1.
		Vector4[] values = [V(0), V(0), V(1), V(0), V(0), V(0)];
		var channel = new AnimationChannel(0, AnimationPath.Translation, AnimationInterpolation.CubicSpline, [0f, 2f], values);
		var s = 0.25f;
		Assert.Equal((s * s * s - 2 * s * s + s) * 2f, channel.Sample(0.5f).X, 5);

		// With zero tangents the curve is a smoothstep between the values, and passes through them at the keys.
		var smooth = new AnimationChannel(0, AnimationPath.Translation, AnimationInterpolation.CubicSpline, [0f, 1f], [V(0), V(4), V(0), V(0), V(8), V(0)]);
		Assert.Equal(4f, smooth.Sample(0f).X);
		Assert.Equal(6f, smooth.Sample(0.5f).X, 5);
		Assert.Equal(4f + 4f * (3 * 0.04f - 2 * 0.008f), smooth.Sample(0.2f).X, 5);
		Assert.Equal(8f, smooth.Sample(1f).X);
	}

	[Fact]
	public void ChannelsRefuseBadKeys()
	{
		Assert.Throws<ArgumentException>(() => Move(0, [1f, 0.5f], 0f, 1f));
		Assert.Throws<ArgumentException>(() => new AnimationChannel(0, AnimationPath.Translation, AnimationInterpolation.CubicSpline, [0f, 1f], [V(0), V(1)]));
		Assert.Throws<ArgumentException>(() => new AnimationChannel(0, AnimationPath.Translation, AnimationInterpolation.Linear, [], []));
	}

	[Fact]
	public void AClipWritesOnlyWhatItAnimatesAndClampsItsTime()
	{
		var clip = new AnimationClip("slide", [Move(1, [0f, 2f], 0f, 4f)]);
		Assert.Equal(2f, clip.Duration);
		Assert.Equal(2, clip.JointSpan);

		var pose = new Transform[3];
		for (var i = 0; i < pose.Length; i++) pose[i] = new Transform(new Vector3(9, 9, 9), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1f));
		clip.Sample(1f, pose);
		Assert.Equal(new Vector3(2, 0, 0), pose[1].Position);
		// The rotation and scale of joint 1 and the other joints are untouched.
		Assert.Equal(pose[0].Rotation, pose[1].Rotation);
		Assert.Equal(new Vector3(9, 9, 9), pose[0].Position);
		Assert.Equal(new Vector3(9, 9, 9), pose[2].Position);

		clip.Sample(5f, pose);
		Assert.Equal(4f, pose[1].Position.X);
		clip.Sample(-1f, pose);
		Assert.Equal(0f, pose[1].Position.X);

		// A pose too short for a channel skips it.
		clip.Sample(1f, new Transform[1]);
	}

	[Fact]
	public void ModelMatricesApplyEachJointUnderItsParentWhateverTheOrder()
	{
		// Child listed first: joint 0's parent is joint 1, which hangs under the skeleton's root matrix.
		var root = Matrix4x4.CreateTranslation(0, 5, 0);
		var skeleton = new Skeleton("reversed",
		[
			new SkeletonJoint("hand", 1, new Transform(new Vector3(1, 0, 0)), Matrix4x4.Identity),
			new SkeletonJoint("arm", -1, new Transform(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)), Matrix4x4.Identity),
		], root);

		var model = new Matrix4x4[2];
		skeleton.ComputeModelMatrices(skeleton.CreateRestPose(), model);
		Near(new Vector3(0, 5, 0), model[1].Translation);
		// The arm turned +X to +Y, so the hand is one unit above it.
		Near(new Vector3(0, 6, 0), model[0].Translation);
		Assert.Equal(1, skeleton.IndexOf("arm"));
		Assert.Equal(-1, skeleton.IndexOf("leg"));
	}

	[Fact]
	public void SkeletonsRefuseCyclesAndMissingParents()
	{
		Assert.Throws<ArgumentException>(() => new Skeleton("cycle", [new SkeletonJoint("a", 1, Transform.Identity, Matrix4x4.Identity), new SkeletonJoint("b", 0, Transform.Identity, Matrix4x4.Identity)]));
		Assert.Throws<ArgumentException>(() => new Skeleton("self", [new SkeletonJoint("a", 0, Transform.Identity, Matrix4x4.Identity)]));
		Assert.Throws<ArgumentException>(() => new Skeleton("missing", [new SkeletonJoint("a", 3, Transform.Identity, Matrix4x4.Identity)]));
	}

	[Fact]
	public void TheRestPoseSkinsEveryVertexToWhereItWasBound()
	{
		var skeleton = Chain(4);
		var player = new AnimationPlayer(skeleton);
		foreach (var m in player.SkinningMatrices) Assert.True(m.IsIdentity, $"{m}");
		Near(new Vector3(3, 0, 0), player.JointMatrices[3].Translation);
	}

	[Fact]
	public void SkinningMatricesMoveBoundVerticesWithTheirJoints()
	{
		// Bend the chain's second joint by 90 degrees about z: a vertex bound at x = 2 (joint 2's rest position, one unit
		// past the bend at x = 1) ends up one unit above the bend.
		var skeleton = Chain(3);
		var pose = skeleton.CreateRestPose();
		pose[1].Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
		var model = new Matrix4x4[3];
		var skinning = new Matrix4x4[3];
		skeleton.ComputeModelMatrices(pose, model);
		skeleton.ComputeSkinningMatrices(model, skinning);

		Near(new Vector3(1, 1, 0), Vector3.Transform(new Vector3(2, 0, 0), skinning[2]));
		Near(new Vector3(1, 0.5f, 0), Vector3.Transform(new Vector3(1.5f, 0, 0), skinning[1]));
		// Joint 0 did not move.
		Near(new Vector3(0.5f, 0, 0), Vector3.Transform(new Vector3(0.5f, 0, 0), skinning[0]));
	}

	[Fact]
	public void BlendingLerpsPositionsAndSlerpsRotations()
	{
		var a = new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One);
		var b = new Transform(new Vector3(2, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), new Vector3(3));
		var half = AnimationPose.Blend(a, b, 0.5f);
		Near(new Vector3(1, 0, 0), half.Position);
		Near(new Vector3(2), half.Scale);
		Near(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4), half.Rotation);

		Span<Transform> result = new Transform[2];
		AnimationPose.Blend([a, a], [b, b], 1f, result);
		Near(b.Position, result[1].Position);
	}

	[Fact]
	public void ThePlayerLoopsOrHoldsAndReportsTheEnd()
	{
		var clip = new AnimationClip("slide", [Move(0, [0f, 1f], 0f, 10f)]);
		var player = new AnimationPlayer(Chain(1));
		player.Play(clip);
		player.Update(0.25f);
		Assert.Equal(2.5f, player.Pose[0].Position.X, 4);
		player.Update(1f);
		Assert.Equal(0.25f, player.Time, 4);
		Assert.False(player.IsFinished);

		player.Play(clip, loop: false);
		player.Update(3f);
		Assert.Equal(1f, player.Time);
		Assert.True(player.IsFinished);
		Assert.Equal(10f, player.Pose[0].Position.X);

		// Backwards from the end.
		player.Play(clip, loop: false, speed: -2f);
		Assert.Equal(1f, player.Time);
		player.Update(0.25f);
		Assert.Equal(0.5f, player.Time, 4);

		player.Stop();
		Assert.Null(player.Clip);
		Assert.Equal(Vector3.Zero, player.Pose[0].Position);
	}

	[Fact]
	public void ACrossFadeBlendsBothClipsThenHandsOver()
	{
		var left = new AnimationClip("left", [Move(0, [0f, 1f], -4f, -4f)]);
		var right = new AnimationClip("right", [Move(0, [0f, 1f], 4f, 4f)]);
		var player = new AnimationPlayer(Chain(1));
		player.Play(left);
		player.CrossFade(right, 1f);
		Assert.Same(right, player.Next);

		player.Update(0.5f);
		Assert.Equal(0.5f, player.FadeWeight, 4);
		Assert.Equal(0f, player.Pose[0].Position.X, 4);
		Assert.Equal(0.5f, player.NextTime, 4);

		player.Update(0.25f);
		Assert.Equal(2f, player.Pose[0].Position.X, 4);

		player.Update(0.5f);
		Assert.Same(right, player.Clip);
		Assert.Null(player.Next);
		Assert.Equal(4f, player.Pose[0].Position.X);
		// The faded-in clip kept its own time.
		Assert.Equal(0.25f, player.Time, 4);

		// Without a clip playing, a cross-fade plays at once.
		var fresh = new AnimationPlayer(Chain(1));
		fresh.CrossFade(left, 1f);
		Assert.Same(left, fresh.Clip);
		Assert.Null(fresh.Next);
	}

	[Fact]
	public void ThePlayerRefusesAClipForABiggerSkeleton()
	{
		var player = new AnimationPlayer(Chain(2));
		var thrown = Assert.Throws<ArgumentException>(() => player.Play(new AnimationClip("long", [Move(5, [0f], 0f)])));
		Assert.Contains("joint 5", thrown.Message);
	}

	[Fact]
	public void SkinnedMeshDataNeedsJointsAndWeightsTogether()
	{
		var mesh = new MeshData("tri", [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]) { Joints = new JointIndices[3] };
		Assert.False(mesh.IsSkinned);
		Assert.Throws<InvalidOperationException>(mesh.Validate);
		mesh.Weights = [Vector4.UnitX, Vector4.UnitX, Vector4.UnitX];
		mesh.Validate();
		Assert.True(mesh.IsSkinned);
		Assert.True(mesh.Attributes.HasFlag(VertexAttributes.Skin));
		mesh.Weights = [Vector4.UnitX];
		Assert.Throws<InvalidOperationException>(mesh.Validate);
		Assert.Equal((ushort)7, new JointIndices(1, 3, 5, 7)[3]);
	}
}
