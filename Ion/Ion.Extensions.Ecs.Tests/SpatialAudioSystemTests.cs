using Arch.Core;
using Arch.Core.Extensions;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Audio;
using Ion.Extensions.Graphics;
using Ion.Testing;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Ecs.Tests;

/// <summary><see cref="SpatialAudioSystem"/>: the listener and the emitters follow their entities.</summary>
public class SpatialAudioSystemTests
{
	private static IonTestHost Host() => new IonTestHost()
		.Configure(services => services.AddEcs())
		.ConfigureApp(app => app.UseEcsAudio());

	/// <summary>The left and right peaks of the audio rendered since the last call.</summary>
	private static (float Left, float Right) Peaks(NullAudioOutput output)
	{
		var captured = output.Captured;
		output.ClearCapture();
		float left = 0, right = 0;
		for (var i = captured.Length / 2; i < captured.Length / 2 * 2; i += 2)
		{
			left = Math.Max(left, Math.Abs(captured[i]));
			right = Math.Max(right, Math.Abs(captured[i + 1]));
		}

		return (left, right);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void A2DEmitterIsHeardFromWhereItsEntityIsRelativeToTheListener()
	{
		using var host = Host().Start();
		var world = host.Get<World>();
		var spatial = host.Get<ISpatialAudio>();
		var output = host.Get<NullAudioOutput>();
		output.CaptureEnabled = true;

		var camera = world.Create(new Transform2D(new Vector2(400, 300)), new AudioListener());
		var hum = new SoundEffect("hum", Enumerable.Repeat(0.5f, 48000).ToArray(), 1, 48000);
		var voice = spatial.Play2D(hum, new Vector2(400, 300), loop: true, attenuation: new Attenuation(MinDistance: 64f, Model: DistanceModel.None));
		var car = world.Create(new Transform2D(new Vector2(800, 300)), new AudioEmitter(voice));
		host.Step(10);

		Assert.Equal(new Vector3(400, 300, 0), spatial.ListenerPosition);
		var (left, right) = Peaks(output);
		Assert.Equal(0f, left, 3);
		Assert.Equal(0.5f, right, 3);

		// The car drives to the left of the camera: it is heard on the left.
		car.Get<Transform2D>().Position = new Vector2(0, 300);
		host.Step(10);
		(left, right) = Peaks(output);
		Assert.Equal(0.5f, left, 3);
		Assert.Equal(0f, right, 3);

		// The camera moves past it: the listener follows its entity, and the car is on the right again.
		camera.Get<Transform2D>().Position = new Vector2(-400, 300);
		host.Step(10);
		(left, right) = Peaks(output);
		Assert.Equal(0f, left, 3);
		Assert.Equal(0.5f, right, 3);
		Assert.True(spatial.IsPositional(voice));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void A3DListenerTakesItsEntitysPositionAndFacing()
	{
		using var host = Host().Start();
		var world = host.Get<World>();
		var spatial = host.Get<ISpatialAudio>();

		// Turned a quarter to the left (about +y): it faces -x.
		world.Create(new Transform(new Vector3(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)), new AudioListener());
		host.Step(2);

		Assert.Equal(new Vector3(1, 2, 3), spatial.ListenerPosition);
		Assert.True(Vector3.Distance(-Vector3.UnitX, spatial.ListenerForward) < 1e-5f, $"{spatial.ListenerForward}");
		Assert.True(Vector3.Distance(Vector3.UnitY, spatial.ListenerUp) < 1e-5f);

		// So a sound further along -z is on its right.
		Assert.Equal(1f, spatial.Evaluate(new Vector3(1, 2, -10), Attenuation.Default).Pan, 3);
	}
}
