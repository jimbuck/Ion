using System.Numerics;

using static Ion.Extensions.Audio.Tests.AudioTestUtils;

namespace Ion.Extensions.Audio.Tests;

/// <summary>Positional audio: gain against distance, pan against direction, in 3D and 2D, and through the mixer.</summary>
public class SpatialAudioTests
{
	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(DistanceModel.Inverse, 0.5f, 1f)]   // inside the minimum distance: full
	[InlineData(DistanceModel.Inverse, 2f, 0.5f)]
	[InlineData(DistanceModel.Inverse, 4f, 0.25f)]
	[InlineData(DistanceModel.Inverse, 1000f, 0.01f)] // clamped at the maximum (100)
	[InlineData(DistanceModel.Linear, 50.5f, 0.5f)]
	[InlineData(DistanceModel.Linear, 200f, 0f)]
	[InlineData(DistanceModel.Exponential, 4f, 0.25f)]
	[InlineData(DistanceModel.None, 80f, 1f)]
	public void GainFallsOffWithDistance(DistanceModel model, float distance, float expected)
	{
		var attenuation = new Attenuation(MinDistance: 1f, MaxDistance: 100f, Model: model);
		Assert.Equal(expected, attenuation.GainAt(distance), 4);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheRolloffScalesTheFalloff()
	{
		Assert.Equal(1f / 3f, new Attenuation(Rolloff: 2f).GainAt(2f), 4);
		Assert.Equal(1f, new Attenuation(Rolloff: 0f).GainAt(50f));
		Assert.Equal(0f, new Attenuation(MaxDistance: 100f, Model: DistanceModel.Linear).GainAt(float.PositiveInfinity));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void PanFollowsTheDirectionFromTheListener()
	{
		var spatial = new SpatialAudio(Mixer());
		var attenuation = Attenuation.Default;

		// Facing -z with +y up (a camera's default): +x is to the right.
		Assert.Equal(1f, spatial.Evaluate(new Vector3(10, 0, 0), attenuation).Pan, 4);
		Assert.Equal(-1f, spatial.Evaluate(new Vector3(-10, 0, 0), attenuation).Pan, 4);
		Assert.Equal(0f, spatial.Evaluate(new Vector3(0, 0, -10), attenuation).Pan, 4);
		Assert.Equal(0f, spatial.Evaluate(new Vector3(0, 10, 0), attenuation).Pan, 4);
		Assert.Equal(MathF.Sqrt(0.5f), spatial.Evaluate(new Vector3(10, 0, -10), attenuation).Pan, 4);

		// Within the minimum distance the pan narrows: half a unit to the right is half right.
		Assert.Equal(0.5f, spatial.Evaluate(new Vector3(0.5f, 0, 0), attenuation).Pan, 4);
		Assert.Equal(0f, spatial.Evaluate(Vector3.Zero, attenuation).Pan);

		// Turned around (facing +z), and moved: +x is now to the left, measured from the listener.
		spatial.SetListener(new Vector3(100, 0, 0), Vector3.UnitZ, Vector3.UnitY);
		Assert.Equal(-1f, spatial.Evaluate(new Vector3(110, 0, 0), attenuation).Pan, 4);
		Assert.Equal(0.1f, spatial.Evaluate(new Vector3(110, 0, 0), attenuation).Gain, 4);

		spatial.PanStrength = 0.5f;
		Assert.Equal(-0.5f, spatial.Evaluate(new Vector3(110, 0, 0), attenuation).Pan, 4);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void InTwoDimensionsScreenRightIsRight()
	{
		var spatial = new SpatialAudio(Mixer());
		spatial.SetListener2D(new Vector2(400, 300));
		var pixels = Attenuation.Pixels;

		Assert.Equal(1f, spatial.Evaluate(new Vector3(800, 300, 0), pixels).Pan, 4);
		Assert.Equal(-1f, spatial.Evaluate(new Vector3(0, 300, 0), pixels).Pan, 4);
		Assert.Equal(0f, spatial.Evaluate(new Vector3(400, 100, 0), pixels).Pan, 4);
		Assert.Equal(64f / 400f, spatial.Evaluate(new Vector3(800, 300, 0), pixels).Gain, 4);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AMovingVoiceIsMixedWhereItIs()
	{
		var mixer = Mixer();
		var spatial = new SpatialAudio(mixer);

		// A constant 1.0 one unit to the right: full gain, right only.
		var voice = spatial.Play(Constant(1f, Rate), new Vector3(1, 0, 0), loop: true);
		var right = Render(mixer, 256);
		Assert.Equal(0f, right[200], 4);
		Assert.Equal(1f, right[201], 4);

		// Moved two units to the left: half the gain, left only (after the one-buffer ramp).
		spatial.SetPosition(voice, new Vector3(-2, 0, 0));
		spatial.Update();
		Render(mixer, 256);
		var left = Render(mixer, 256);
		Assert.Equal(0.5f, left[100], 4);
		Assert.Equal(0f, left[101], 4);

		// The volume before the falloff.
		spatial.SetVolume(voice, 0.5f);
		spatial.Update();
		Render(mixer, 256);
		Assert.Equal(0.25f, Render(mixer, 256)[100], 4);
		Assert.True(spatial.IsPositional(voice));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EndedVoicesAreForgottenAndOthersAreLeftAlone()
	{
		var mixer = Mixer();
		var spatial = new SpatialAudio(mixer);
		var voice = spatial.Play(Constant(1f, 100), new Vector3(3, 0, 0));
		var plain = mixer.Play(Constant(1f, Rate), loop: true);
		Render(mixer, 512);
		mixer.Flush();
		spatial.Update();
		Assert.Equal(0, spatial.Count);
		Assert.False(spatial.IsPositional(voice));

		// A voice not started as positional is not moved.
		spatial.SetPosition(plain, new Vector3(100, 0, 0));
		Assert.False(spatial.IsPositional(plain));
	}
}
