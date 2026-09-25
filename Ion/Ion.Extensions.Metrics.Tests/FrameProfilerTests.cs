namespace Ion.Tests;

public class FrameProfilerTests
{
	private static readonly SpanId Work = MetricsIds.Register("Tests.Work");
	private static readonly SpanId Other = MetricsIds.Register("Tests.Other");

	[Fact, Trait(CATEGORY, UNIT)]
	public void SpanIdsAreInternedOnce()
	{
		Assert.Equal(Work, MetricsIds.Register("Tests.Work"));
		Assert.NotEqual(Work, Other);
		Assert.Equal("Tests.Work", Work.Name);
		Assert.True(MetricsIds.TryGet("Tests.Other", out var other));
		Assert.Equal(Other, other);
		Assert.Equal("?", new SpanId(int.MaxValue).Name);
		Assert.False(SpanId.None.IsValid);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheProfilingFeatureSwitchIsOnByDefault()
	{
		Assert.True(FrameProfiler.IsProfilingEnabled);
		Assert.True(AppContext.TryGetSwitch(FrameProfiler.ProfilingSwitchName, out var enabled) && enabled);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheRingKeepsTheLastFramesAndWraps()
	{
		var profiler = new FrameProfiler(historyFrames: 4, spansPerFrame: 8) { IsActive = true };

		for (uint frame = 0; frame < 10; frame++)
		{
			profiler.BeginFrame(frame);
			using (profiler.Scope(Work)) { }
			var stats = new FrameStats { FixedSteps = (int)frame };
			profiler.EndFrame(ref stats);
		}

		Assert.Equal(10, profiler.FramesCompleted);
		Assert.Equal(4, profiler.Count);
		Assert.Equal(9u, profiler.GetFrame(0).Frame);
		Assert.Equal(6u, profiler.GetFrame(3).Frame);
		Assert.Throws<ArgumentOutOfRangeException>(() => profiler.GetFrame(4));
		Assert.Equal(9u, profiler.LastFrame.Frame);
		Assert.Equal(9, profiler.LastFrame.FixedSteps);

		var frames = new List<FrameProfile>();
		Assert.Equal(4, profiler.CopyFrames(frames));
		Assert.Equal([6u, 7u, 8u, 9u], frames.Select(f => f.Frame));
		Assert.All(frames, f => Assert.Equal(Work, Assert.Single(f.Spans.ToArray()).Id));

		profiler.Clear();
		Assert.Equal(0, profiler.Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RecordingNeverAllocatesAfterWarmUp()
	{
		var profiler = new FrameProfiler(historyFrames: 16, spansPerFrame: 32) { IsActive = true };
		void RunFrames(int count)
		{
			for (var i = 0; i < count; i++)
			{
				profiler.BeginFrame((uint)profiler.FramesCompleted);
				for (var s = 0; s < 40; s++)
				{
					using var _ = profiler.Scope(s % 2 == 0 ? Work : Other);
				}

				var start = FrameProfiler.IsProfilingEnabled ? profiler.Begin(Work) : 0L;
				if (FrameProfiler.IsProfilingEnabled) profiler.End(Work, start);
				var stats = new FrameStats { FixedSteps = 1 };
				profiler.EndFrame(ref stats);
			}
		}

		RunFrames(40); // Wraps the ring more than twice.
		var before = GC.GetAllocatedBytesForCurrentThread();
		RunFrames(1000);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Equal(32, profiler.GetFrame(0).Spans.Length);
		Assert.Equal(9, profiler.GetFrame(0).DroppedSpans);
		Assert.Equal(9, profiler.LastFrame.DroppedSpans);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AnInactiveProfilerRecordsNothingAndScopesAreNoOps()
	{
		var profiler = new FrameProfiler(historyFrames: 2, spansPerFrame: 8);
		Assert.False(profiler.IsActive);
		profiler.BeginFrame(0);

		var scope = profiler.Scope(Work);
		Assert.False(scope.IsRecording);
		scope.Dispose();
		Assert.Equal(0, profiler.Begin(Work));
		profiler.End(Work, 0);
		profiler.EndFrame();

		Assert.Equal(0, profiler.GetFrame(0).Spans.Length);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheDisabledProfilerKeepsNothing()
	{
		var profiler = FrameProfiler.Disabled;
		profiler.IsActive = true;

		Assert.False(profiler.IsEnabled);
		Assert.False(profiler.CanRecord);
		Assert.False(profiler.IsActive);
		profiler.BeginFrame(1);
		profiler.EndFrame();
		Assert.Equal(0, profiler.Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EndFrameAddsTheGcAndAllocationDeltas()
	{
		var profiler = new FrameProfiler(historyFrames: 4, spansPerFrame: 0);
		profiler.BeginFrame(0);
		profiler.EndFrame();

		profiler.BeginFrame(1);
		var garbage = new byte[100_000];
		GC.KeepAlive(garbage);
		GC.Collect(0);
		profiler.EndFrame();

		Assert.True(profiler.LastFrame.AllocatedBytes >= 100_000, profiler.LastFrame.AllocatedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
		Assert.True(profiler.LastFrame.Gc0 >= 1);
		Assert.True(profiler.LastFrame.FrameMs >= 0);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ListenersSeeEveryFrameAndSinksSeeEverySpan()
	{
		var profiler = new FrameProfiler(historyFrames: 4, spansPerFrame: 8) { IsActive = true };
		var listener = new Listener();
		var sink = new Sink();
		profiler.AddListener(listener);
		profiler.SpanSink = sink;

		profiler.BeginFrame(3);
		using (profiler.Scope(Work)) { }
		profiler.EndFrame();
		profiler.RemoveListener(listener);
		profiler.BeginFrame(4);
		profiler.EndFrame();

		Assert.Equal([3u], listener.Frames);
		Assert.Equal(["+Tests.Work", "-Tests.Work", "frame", "frame"], sink.Calls);
	}

#pragma warning disable CS0618 // The obsolete adapter.
	[Fact, Trait(CATEGORY, UNIT)]
	public void TheObsoleteTraceTimerAdapterReusesItsInstances()
	{
		var profiler = new FrameProfiler(historyFrames: 2, spansPerFrame: 64) { IsActive = true };
		var timer = new Ion.Extensions.Debug.TraceTimerAdapter(profiler, "Legacy");
		void Frame()
		{
			profiler.BeginFrame((uint)profiler.FramesCompleted);
			var outer = timer.Start("Outer");
			timer.Start("Inner").Stop();
			outer.Then("Next");
			outer.Stop();
			profiler.EndFrame();
		}

		Frame();
		Assert.Equal(["Legacy::Inner", "Legacy::Outer", "Legacy::Next"], profiler.GetFrame(0).Spans.ToArray().Select(s => s.Id.Name));

		for (var i = 0; i < 10; i++) Frame();
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 100; i++) Frame();
		Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
	}
#pragma warning restore CS0618

	private sealed class Listener : IFrameListener
	{
		public List<uint> Frames { get; } = [];

		public void OnFrame(FrameProfile frame) => Frames.Add(frame.Frame);
	}

	private sealed class Sink : ISpanSink
	{
		public List<string> Calls { get; } = [];

		public void Begin(SpanId span) => Calls.Add("+" + span.Name);

		public void End(SpanId span) => Calls.Add("-" + span.Name);

		public void FrameMark() => Calls.Add("frame");
	}
}
