namespace Ion.Extensions.Graphics.Abstractions.Tests;

/// <summary>The input method composition state of the tracker, and its recording.</summary>
public class InputCompositionTests
{
	[Fact]
	public void ACompositionIsALevelStateUntilChangedOrEnded()
	{
		var input = new InputTracker();
		input.BeginFrame();
		Assert.Equal("", input.Composition);
		input.OnComposition("にほ", 1);
		Assert.Equal("にほ", input.Composition);
		Assert.Equal(1, input.CompositionCursor);

		// It stays across frames, unlike text input.
		input.BeginFrame();
		Assert.Equal("にほ", input.Composition);

		// The input method commits: the text arrives, then the composition ends.
		input.BeginFrame();
		input.OnText("日本");
		input.OnComposition("", 0);
		Assert.Equal("日本", input.Text.ToString());
		Assert.Equal("", input.Composition);

		// A cursor past the text is clamped; losing focus cancels a composition.
		input.OnComposition("ab", 9);
		Assert.Equal(2, input.CompositionCursor);
		input.ReleaseAll();
		Assert.Equal("", input.Composition);
	}

	[Fact]
	public void CompositionEventsAreRecordedAndPlayedBack()
	{
		using var stream = new MemoryStream();
		var recorded = new InputTracker();
		using (var recorder = new InputRecorder(stream, leaveOpen: true))
		{
			recorded.Recorder = recorder;
			recorded.BeginFrame();
			recorded.OnComposition("한", 1);
			recorded.BeginFrame();
			recorded.OnText('한');
			recorded.OnComposition("", 0);
			recorded.BeginFrame();
		}

		stream.Position = 0;
		var played = new InputTracker { Playback = new InputPlayer(stream, leaveOpen: true) };
		played.BeginFrame();
		Assert.Equal("한", played.Composition);
		played.BeginFrame();
		Assert.Equal("", played.Composition);
		Assert.Equal("한", played.Text.ToString());

		// The event itself, through a sink.
		var sink = new InputTracker();
		sink.BeginFrame();
		InputEvent.ForComposition("xyz", 2).ApplyTo(sink);
		Assert.Equal(("xyz", 2), (sink.Composition, sink.CompositionCursor));
	}
}
