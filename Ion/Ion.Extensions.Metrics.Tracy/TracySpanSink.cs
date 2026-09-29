using System.Runtime.InteropServices;

using bottlenoselabs.C2CS.Runtime;

using Microsoft.Extensions.DependencyInjection;

using static Tracy.PInvoke;

namespace Ion.Extensions.Metrics.Tracy;

/// <summary>
/// Streams the frame profiler to a Tracy server: every span as a Tracy zone (live, on the thread that records it), a frame
/// mark per frame, and the frame counters as plots. Span names get one static Tracy source location each, allocated in
/// native memory the first time the span is seen, so emitting a zone allocates nothing.
/// </summary>
public sealed unsafe class TracySpanSink : ISpanSink, IFrameListener
{
	private const int MaxDepth = 256;

	[ThreadStatic] private static TracyCZoneCtx[]? t_zones;
	[ThreadStatic] private static int t_depth;

	private readonly Lock _lock = new();
	private TracySourceLocationData*[] _locations = new TracySourceLocationData*[256];
	private readonly CString _file = Utf8("Ion");
	private readonly CString _frameMs = Utf8("frame_ms");
	private readonly CString _drawCalls = Utf8("draw_calls");
	private readonly CString _sprites = Utf8("sprites");
	private readonly CString _events = Utf8("events_emitted");
	private readonly CString _allocated = Utf8("allocated_bytes");

	/// <inheritdoc/>
	public void Begin(SpanId span)
	{
		var zones = t_zones ??= new TracyCZoneCtx[MaxDepth];
		var context = TracyEmitZoneBegin(Location(span), 1);
		if (t_depth < MaxDepth) zones[t_depth] = context;
		t_depth++;
	}

	/// <inheritdoc/>
	public void End(SpanId span)
	{
		if (t_depth == 0) return;
		t_depth--;
		if (t_depth < MaxDepth) TracyEmitZoneEnd(t_zones![t_depth]);
	}

	/// <inheritdoc/>
	public void FrameMark() => TracyEmitFrameMark(default);

	/// <inheritdoc/>
	public void OnFrame(FrameProfile frame)
	{
		if (frame.Kind != FrameKind.Frame) return;

		ref readonly var stats = ref frame.Stats;
		TracyEmitPlot(_frameMs, stats.FrameMs);
		TracyEmitPlotInt(_drawCalls, stats.DrawCalls);
		TracyEmitPlotInt(_sprites, stats.Sprites);
		TracyEmitPlotInt(_events, stats.EventsEmitted);
		TracyEmitPlotInt(_allocated, stats.AllocatedBytes);
	}

	private TracySourceLocationData* Location(SpanId span)
	{
		var locations = Volatile.Read(ref _locations);
		var id = span.Value;
		if ((uint)id < (uint)locations.Length && locations[id] is var location && location is not null) return location;
		return CreateLocation(span);
	}

	private TracySourceLocationData* CreateLocation(SpanId span)
	{
		lock (_lock)
		{
			var id = Math.Max(span.Value, 0);
			if (id >= _locations.Length)
			{
				var grown = new TracySourceLocationData*[Math.Max(id + 1, _locations.Length * 2)];
				Array.Copy(_locations, grown, _locations.Length);
				Volatile.Write(ref _locations, grown);
			}

			if (_locations[id] is not null) return _locations[id];

			// Tracy keeps a pointer to the source location for the life of the process: it is never freed.
			var location = (TracySourceLocationData*)NativeMemory.AllocZeroed((nuint)sizeof(TracySourceLocationData));
			var name = Utf8(span.Name);
			location->_Name = name;
			location->_Function = name;
			location->_File = _file;
			location->Line = 0;
			location->Color = 0;
			_locations[id] = location;
			return location;
		}
	}

	private static CString Utf8(string value) => new(Marshal.StringToCoTaskMemUTF8(value));
}

/// <summary>Registration of the Tracy bridge.</summary>
public static class TracyExtensions
{
	/// <summary>
	/// Streams the application's frame profiler to Tracy: attaches a <see cref="TracySpanSink"/> as its span sink and frame
	/// listener, and turns profiling on (spans are only produced while it is on). Call it after the metrics are registered
	/// (<c>AddMetrics</c>, or <c>AddIon</c>); guard the call with <c>#if TRACY</c> so builds without Tracy do not load it.
	/// </summary>
	public static IIonApplication UseMetricsTracy(this IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);
		RegisterResolver();

		var profiler = app.Services.GetRequiredService<FrameProfiler>();
		var sink = new TracySpanSink();
		profiler.SpanSink = sink;
		profiler.AddListener(sink);
		profiler.IsActive = true;
		return app;
	}

	private static int _resolverRegistered;

	/// <summary>
	/// Finds TracyClient next to the application. The JIT host resolves it from the package's native assets, but a
	/// NativeAOT app has no deps.json and dlopen does not search the application directory.
	/// </summary>
	private static void RegisterResolver()
	{
		if (Interlocked.Exchange(ref _resolverRegistered, 1) != 0) return;

		NativeLibrary.SetDllImportResolver(typeof(global::Tracy.PInvoke).Assembly, static (name, assembly, searchPath) =>
		{
			if (name != "TracyClient") return IntPtr.Zero;

			var file = OperatingSystem.IsWindows() ? "TracyClient.dll" : OperatingSystem.IsMacOS() ? "libTracyClient.dylib" : "TracyClient.so";
			var local = Path.Combine(AppContext.BaseDirectory, file);
			if (NativeLibrary.TryLoad(local, out var handle)) return handle;
			return NativeLibrary.TryLoad(name, assembly, searchPath, out handle) ? handle : IntPtr.Zero;
		});
	}
}
