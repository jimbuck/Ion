using System.Runtime.InteropServices;


namespace Ion.Extensions.Graphics;

/// <summary>
/// Resolves <see cref="GraphicsConfig.PreferredBackend"/> to one of the RHI backends that are registered: an explicit
/// <see cref="GraphicsBackend.Vulkan"/> or <see cref="GraphicsBackend.OpenGLES"/> is used as given (forced, even when it
/// then fails to start), and <see cref="GraphicsBackend.Auto"/> (or any backend that is not registered) picks the first
/// available backend in the platform order <see cref="AutoOrder()"/>.
/// </summary>
/// <remarks>
/// The platform order is Vulkan then OpenGL ES on desktop (Windows, macOS through MoltenVK, Linux x64) and Android, and
/// OpenGL ES first on Linux arm64, where the handhelds this engine targets (the R36S: RK3326, Mali-G31, Panfrost) have a
/// stable GLES driver and only an experimental Vulkan one. Availability probes run once, on the first
/// <see cref="Resolve"/> of <see cref="GraphicsBackend.Auto"/>.
/// </remarks>
public sealed class GraphicsBackendSelector
{
	private readonly Dictionary<GraphicsBackend, Func<bool>> _probes;
	private readonly IReadOnlyList<GraphicsBackend> _order;
	private readonly Lock _lock = new();
	private GraphicsBackend? _auto;

	/// <summary>
	/// Creates a selector over the registered backends in <paramref name="probes"/> (each with a probe that tells whether it
	/// can start on this machine), tried in <paramref name="order"/> (<see cref="AutoOrder()"/> by default).
	/// </summary>
	public GraphicsBackendSelector(IReadOnlyDictionary<GraphicsBackend, Func<bool>> probes, IReadOnlyList<GraphicsBackend>? order = null)
	{
		ArgumentNullException.ThrowIfNull(probes);
		if (probes.Count == 0) throw new ArgumentException("At least one backend must be registered.", nameof(probes));
		_probes = new Dictionary<GraphicsBackend, Func<bool>>(probes);
		_order = order ?? AutoOrder();
	}

	/// <summary>The backend passed to the first <see cref="Resolve"/> (the configured preference, for logs), or null.</summary>
	public GraphicsBackend? FirstRequested { get; private set; }

	/// <summary>The backends this selector can pick, in the order <see cref="GraphicsBackend.Auto"/> tries them.</summary>
	public IEnumerable<GraphicsBackend> Candidates
	{
		get
		{
			foreach (var backend in _order)
			{
				if (_probes.ContainsKey(backend)) yield return backend;
			}

			foreach (var backend in _probes.Keys)
			{
				if (!_order.Contains(backend)) yield return backend;
			}
		}
	}

	/// <summary>
	/// The backend to use for <paramref name="preferred"/>: itself when it is registered, otherwise (for
	/// <see cref="GraphicsBackend.Auto"/> and unregistered backends) the first registered backend in platform order whose
	/// probe succeeds, or the first registered one when no probe succeeds (so its own error explains what is missing).
	/// </summary>
	public GraphicsBackend Resolve(GraphicsBackend preferred)
	{
		FirstRequested ??= preferred;
		if (preferred != GraphicsBackend.Auto && _probes.ContainsKey(preferred)) return preferred;
		lock (_lock)
		{
			if (_auto is { } cached) return cached;
			GraphicsBackend? first = null;
			foreach (var candidate in Candidates)
			{
				first ??= candidate;
				bool available;
				try
				{
					available = _probes[candidate]();
				}
				catch (Exception)
				{
					available = false;
				}

				if (!available) continue;
				_auto = candidate;
				return candidate;
			}

			_auto = first!.Value;
			return _auto.Value;
		}
	}

	/// <summary>The <see cref="GraphicsBackend.Auto"/> order of this process's platform.</summary>
	public static IReadOnlyList<GraphicsBackend> AutoOrder() =>
		AutoOrder(OperatingSystem.IsLinux(), OperatingSystem.IsAndroid(), RuntimeInformation.ProcessArchitecture);

	/// <summary>
	/// The <see cref="GraphicsBackend.Auto"/> order for a platform: OpenGL ES then Vulkan on Linux arm64 (not Android),
	/// Vulkan then OpenGL ES everywhere else.
	/// </summary>
	public static IReadOnlyList<GraphicsBackend> AutoOrder(bool isLinux, bool isAndroid, Architecture architecture) =>
		isLinux && !isAndroid && architecture == Architecture.Arm64
			? [GraphicsBackend.OpenGLES, GraphicsBackend.Vulkan]
			: [GraphicsBackend.Vulkan, GraphicsBackend.OpenGLES];
}

/// <summary>
/// An RHI graphics service (the Vulkan or GLES backend, or a selection between them): the device, the frame driver and
/// the lifecycle the graphics system calls.
/// </summary>
public interface IRhiGraphics : IGraphicsFrame, IScreenshotSource, IDisposable
{
	/// <summary>The frame driver, or null before <see cref="Initialize"/>.</summary>
	GraphicsFrameDriver? Driver { get; }

	/// <summary>Creates the device and the frame driver (the graphics Init step).</summary>
	void Initialize();

	/// <summary>Starts a frame (the start of the Render stage).</summary>
	void BeginFrame();

	/// <summary>Ends and presents a frame (the end of the Render stage).</summary>
	void EndFrame();
}
