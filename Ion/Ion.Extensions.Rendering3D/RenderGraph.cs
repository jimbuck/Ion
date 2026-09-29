using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering3D;

/// <summary>A texture of a <see cref="RenderGraph"/>: an index into the frame's resource table. <c>default</c> is none.</summary>
/// <param name="Index">The index plus one (0: none).</param>
public readonly record struct RenderGraphTexture(int Index)
{
	/// <summary>True for a handle that names a texture.</summary>
	public bool IsValid => Index > 0;
}

/// <summary>Describes a transient texture: allocated for the passes that use it and recycled after the last one.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Format">The format.</param>
/// <param name="Usage">The usages (<see cref="TextureUsage.RenderAttachment"/> is always added).</param>
public readonly record struct RenderGraphTextureDescriptor(uint Width, uint Height, TextureFormat Format, TextureUsage Usage = TextureUsage.RenderAttachment);

/// <summary>The well-known texture names the 3D renderer registers every frame.</summary>
public static class RenderGraphResources
{
	/// <summary>The frame's color target (the window or offscreen target), imported as an output.</summary>
	public const string Backbuffer = "Backbuffer";

	/// <summary>The directional light's shadow map (imported, persistent).</summary>
	public const string ShadowMap = "ShadowMap";

	/// <summary>The depth buffer of the first camera that renders into the frame (transient).</summary>
	public const string Depth = "Depth";
}

/// <summary>What a pass sees when it executes.</summary>
public readonly ref struct RenderGraphContext
{
	private readonly RenderGraph _graph;
	private readonly RenderGraph.EncoderHolder _encoder;

	internal RenderGraphContext(RenderGraph graph, RenderGraph.EncoderHolder encoder)
	{
		_graph = graph;
		_encoder = encoder;
	}

	/// <summary>The device (null when the graph runs without a GPU, in tests).</summary>
	public IGraphicsDevice? Device => _encoder.Device;

	/// <summary>
	/// The frame's command encoder: begin render passes on it. Created on first use and shared by the passes that follow;
	/// the graph submits it after the last pass (or at <see cref="Flush"/>).
	/// </summary>
	/// <exception cref="InvalidOperationException">The graph runs without a device.</exception>
	public ICommandEncoder Encoder => _encoder.Get();

	/// <summary>
	/// Submits the commands recorded so far, so that work this pass submits itself (the sprite batch of the 2D overlay)
	/// runs after them. Later passes get a new encoder.
	/// </summary>
	public void Flush() => _encoder.Submit();

	/// <summary>The texture behind <paramref name="texture"/> (allocated for this pass when transient).</summary>
	public ITexture GetTexture(RenderGraphTexture texture) => _graph.GetTexture(texture);
}

/// <summary>
/// Declares a pass's resources during <see cref="RenderGraphPass.Setup"/>.
/// </summary>
public sealed class RenderGraphBuilder
{
	private readonly RenderGraph _graph;
	private int _pass;

	internal RenderGraphBuilder(RenderGraph graph) => _graph = graph;

	internal void Begin(int pass) => _pass = pass;

	/// <summary>The texture registered under <paramref name="name"/> this frame, or an invalid handle.</summary>
	public RenderGraphTexture Find(string name) => _graph.Find(name);

	/// <summary>Creates a transient texture, written first by this pass.</summary>
	public RenderGraphTexture CreateTexture(string name, in RenderGraphTextureDescriptor descriptor)
	{
		var texture = _graph.CreateTexture(name, descriptor);
		Write(texture);
		return texture;
	}

	/// <summary>Declares that this pass reads <paramref name="texture"/> (sampling it, or loading it as an attachment).</summary>
	public RenderGraphTexture Read(RenderGraphTexture texture)
	{
		_graph.Declare(_pass, texture, write: false);
		return texture;
	}

	/// <summary>Declares that this pass writes <paramref name="texture"/> (as a render attachment).</summary>
	public RenderGraphTexture Write(RenderGraphTexture texture)
	{
		_graph.Declare(_pass, texture, write: true);
		return texture;
	}

	/// <summary>Declares a read and a write (drawing on top of what earlier passes rendered).</summary>
	public RenderGraphTexture ReadWrite(RenderGraphTexture texture) => Write(Read(texture));

	/// <summary>Keeps this pass even when nothing reads what it writes (it has effects outside the graph).</summary>
	public void HasSideEffects() => _graph.MarkSideEffects(_pass);
}

/// <summary>
/// A pass of the <see cref="RenderGraph"/>: declares the textures it reads and writes in <see cref="Setup"/> and records
/// its commands in <see cref="Execute"/>. Passes are objects reused every frame; <see cref="Setup"/> is called each
/// frame the pass is added.
/// </summary>
/// <remarks>
/// The built-in passes use these orders: shadow 100, depth prepass 200, opaque 300, skybox 400, transparent 500, the 2D
/// overlay 900. A custom pass (a post effect, a debug view) picks an order between them; within the declared
/// dependencies the graph runs passes by ascending order, then in the order they were added.
/// </remarks>
public abstract class RenderGraphPass
{
	/// <summary>The built-in orders.</summary>
	public static class Orders
	{
		/// <summary>Shadow maps.</summary>
		public const int Shadow = 100;
		/// <summary>The depth prepass.</summary>
		public const int DepthPrepass = 200;
		/// <summary>Opaque and alpha-masked objects.</summary>
		public const int Opaque = 300;
		/// <summary>The skybox.</summary>
		public const int Skybox = 400;
		/// <summary>Alpha-blended objects.</summary>
		public const int Transparent = 500;
		/// <summary>Post effects (the first free band).</summary>
		public const int Post = 600;
		/// <summary>The 2D overlay (the sprite batch).</summary>
		public const int Overlay = 900;
	}

	/// <summary>Creates a pass.</summary>
	protected RenderGraphPass(string name, int order)
	{
		Name = name;
		Order = order;
	}

	/// <summary>The pass's name (for logs, tests and profiling).</summary>
	public string Name { get; }

	/// <summary>The pass's order (see <see cref="Orders"/>).</summary>
	public int Order { get; }

	/// <summary>Declares the textures the pass reads and writes.</summary>
	public abstract void Setup(RenderGraphBuilder builder);

	/// <summary>Records the pass's commands.</summary>
	public abstract void Execute(in RenderGraphContext context);

	/// <inheritdoc/>
	public override string ToString() => $"{Name} ({Order})";
}

/// <summary>The lifetime of one transient texture in a compiled graph (for tests and diagnostics).</summary>
/// <param name="Name">The texture's name.</param>
/// <param name="FirstPass">The index in <see cref="RenderGraph.ExecutionOrder"/> of the first pass that uses it.</param>
/// <param name="LastPass">The index of the last pass that uses it.</param>
/// <param name="PhysicalTexture">The index of the pooled texture it was assigned (equal indices alias one texture).</param>
public readonly record struct RenderGraphLifetime(string Name, int FirstPass, int LastPass, int PhysicalTexture);

/// <summary>
/// A small render graph: a DAG of passes with declared texture inputs and outputs, rebuilt every frame without
/// allocating. <see cref="Compile"/> orders the passes (by <see cref="RenderGraphPass.Order"/>, then insertion, which is a
/// topological order of the read-after-write, write-after-write and write-after-read dependencies it derives), culls
/// passes whose results nothing uses, and assigns transient textures to pooled textures by lifetime (textures whose
/// lifetimes do not overlap share one). <see cref="Execute"/> runs the passes in that order.
/// </summary>
public sealed class RenderGraph : IDisposable
{
	private readonly Func<RenderGraphTextureDescriptor, string, ITexture>? _allocate;
	private readonly List<RenderGraphPass> _passes = [];
	private readonly List<Resource> _resources = [];
	private readonly List<Access> _accesses = [];
	private readonly List<bool> _sideEffects = [];
	private readonly List<Pooled> _pool = [];
	private readonly List<RenderGraphPass> _order = [];
	private readonly List<RenderGraphPass> _culled = [];
	private readonly List<RenderGraphLifetime> _lifetimes = [];
	private readonly RenderGraphBuilder _builder;
	private int[] _sorted = [];
	private int[] _position = [];
	private bool[] _alive = [];
	private long _frame;
	private bool _compiled;

	/// <summary>Creates a graph that allocates transient textures on <paramref name="device"/> (none: a graph for tests that never executes on a GPU).</summary>
	public RenderGraph(IGraphicsDevice? device)
		: this(device is null ? null : (descriptor, name) => device.CreateTexture(new TextureDescriptor(descriptor.Width, descriptor.Height, descriptor.Format, descriptor.Usage | TextureUsage.RenderAttachment, Label: name)))
	{
	}

	/// <summary>Creates a graph with a custom transient texture allocator (tests).</summary>
	internal RenderGraph(Func<RenderGraphTextureDescriptor, string, ITexture>? allocate)
	{
		_allocate = allocate;
		_builder = new RenderGraphBuilder(this);
	}

	/// <summary>Frames a pooled texture may stay unused before it is released.</summary>
	public int UnusedFramesBeforeRelease { get; set; } = 3;

	/// <summary>The passes of the last compile, in execution order (culled passes excluded).</summary>
	public IReadOnlyList<RenderGraphPass> ExecutionOrder => _order;

	/// <summary>The passes the last compile culled.</summary>
	public IReadOnlyList<RenderGraphPass> CulledPasses => _culled;

	/// <summary>The transient textures of the last compile.</summary>
	public IReadOnlyList<RenderGraphLifetime> Lifetimes => _lifetimes;

	/// <summary>The number of pooled textures (allocated and kept for later frames).</summary>
	public int PooledTextureCount => _pool.Count;

	/// <summary>Starts a new frame: forgets the passes and resources of the previous one (the pooled textures stay).</summary>
	public void Reset()
	{
		_passes.Clear();
		_resources.Clear();
		_accesses.Clear();
		_sideEffects.Clear();
		_order.Clear();
		_culled.Clear();
		_lifetimes.Clear();
		_compiled = false;
		_frame++;
	}

	/// <summary>
	/// Registers an existing texture under <paramref name="name"/>. An <paramref name="output"/> is a result of the frame
	/// (the backbuffer, a camera's render target): passes that write it are never culled.
	/// </summary>
	public RenderGraphTexture Import(string name, ITexture texture, bool output = false)
	{
		ArgumentNullException.ThrowIfNull(texture);
		_resources.Add(new Resource { Name = name, Imported = texture, Output = output, Physical = -1 });
		return new RenderGraphTexture(_resources.Count);
	}

	/// <summary>Registers a transient texture under <paramref name="name"/>, allocated from the pool while passes use it.</summary>
	public RenderGraphTexture CreateTexture(string name, in RenderGraphTextureDescriptor descriptor)
	{
		_resources.Add(new Resource { Name = name, Descriptor = descriptor, Physical = -1 });
		return new RenderGraphTexture(_resources.Count);
	}

	/// <summary>The texture registered under <paramref name="name"/> this frame (the last one of that name), or an invalid handle.</summary>
	public RenderGraphTexture Find(string name)
	{
		for (var i = _resources.Count - 1; i >= 0; i--)
		{
			if (_resources[i].Name == name) return new RenderGraphTexture(i + 1);
		}

		return default;
	}

	/// <summary>Adds a pass for this frame and runs its <see cref="RenderGraphPass.Setup"/>.</summary>
	public void AddPass(RenderGraphPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		_passes.Add(pass);
		_sideEffects.Add(false);
		_builder.Begin(_passes.Count - 1);
		pass.Setup(_builder);
		_compiled = false;
	}

	internal void Declare(int pass, RenderGraphTexture texture, bool write)
	{
		if (!texture.IsValid || texture.Index > _resources.Count) throw new ArgumentException($"Pass '{_passes[pass].Name}' declared an invalid texture.", nameof(texture));
		_accesses.Add(new Access(pass, texture.Index - 1, write));
	}

	internal void MarkSideEffects(int pass) => _sideEffects[pass] = true;

	/// <summary>
	/// Orders the passes, culls the unused ones and assigns transient textures to pooled ones.
	/// </summary>
	/// <exception cref="InvalidOperationException">A pass reads a transient texture no earlier pass writes.</exception>
	public void Compile()
	{
		var count = _passes.Count;
		if (_sorted.Length < count)
		{
			_sorted = new int[Math.Max(count, _sorted.Length * 2)];
			_position = new int[_sorted.Length];
			_alive = new bool[_sorted.Length];
		}

		// Stable order by (Order, insertion): insertion sort, passes are few.
		for (var i = 0; i < count; i++)
		{
			var j = i;
			while (j > 0 && _passes[_sorted[j - 1]].Order > _passes[i].Order)
			{
				_sorted[j] = _sorted[j - 1];
				j--;
			}

			_sorted[j] = i;
		}

		for (var i = 0; i < count; i++) _position[_sorted[i]] = i;

		// Validate: a transient read needs an earlier writer.
		foreach (var access in _accesses)
		{
			if (access.Write || _resources[access.Resource].Imported is not null) continue;
			var written = false;
			foreach (var other in _accesses)
			{
				if (other.Write && other.Resource == access.Resource && _position[other.Pass] < _position[access.Pass]) { written = true; break; }
			}

			if (!written) throw new InvalidOperationException($"Render graph: pass '{_passes[access.Pass].Name}' reads '{_resources[access.Resource].Name}', which no earlier pass writes.");
		}

		// Cull: walk back from the passes that write outputs or have side effects through what they read (and what they
		// write on top of: a read-write of a resource depends on its earlier writers).
		for (var i = 0; i < count; i++) _alive[i] = false;
		for (var p = count - 1; p >= 0; p--)
		{
			var pass = _sorted[p];
			var root = _sideEffects[pass];
			if (!root)
			{
				foreach (var access in _accesses)
				{
					if (access.Pass == pass && access.Write && _resources[access.Resource].Output) { root = true; break; }
				}
			}

			if (root) _alive[pass] = true;
		}

		// Propagate liveness to the writers a live pass depends on, processing passes from last to first: the latest earlier
		// writer of every resource a live pass reads is live (and, if it read the resource too, its own writer in turn).
		for (var p = count - 1; p >= 0; p--)
		{
			var pass = _sorted[p];
			if (!_alive[pass]) continue;
			foreach (var access in _accesses)
			{
				if (access.Pass != pass || access.Write) continue;
				var writer = -1;
				foreach (var other in _accesses)
				{
					if (other.Write && other.Resource == access.Resource && _position[other.Pass] < p && (writer < 0 || _position[other.Pass] > _position[writer])) writer = other.Pass;
				}

				if (writer >= 0) _alive[writer] = true;
			}
		}

		_order.Clear();
		_culled.Clear();
		for (var p = 0; p < count; p++)
		{
			var pass = _passes[_sorted[p]];
			if (_alive[_sorted[p]]) _order.Add(pass);
			else _culled.Add(pass);
		}

		// Lifetimes of transient textures over the live passes.
		for (var r = 0; r < _resources.Count; r++)
		{
			var resource = _resources[r];
			resource.First = int.MaxValue;
			resource.Last = -1;
			resource.Physical = -1;
			_resources[r] = resource;
		}

		for (var i = 0; i < _order.Count; i++)
		{
			var pass = _passes.IndexOf(_order[i]);
			foreach (var access in _accesses)
			{
				if (access.Pass != pass) continue;
				var resource = _resources[access.Resource];
				resource.First = Math.Min(resource.First, i);
				resource.Last = Math.Max(resource.Last, i);
				_resources[access.Resource] = resource;
			}
		}

		// Assign pooled textures: walk the passes; at a resource's first use take a free pooled texture of the same
		// descriptor (or a new one), and free it after its last use.
		for (var i = 0; i < _pool.Count; i++)
		{
			var pooled = _pool[i];
			pooled.BusyUntil = -1;
			_pool[i] = pooled;
		}

		for (var i = 0; i < _order.Count; i++)
		{
			for (var r = 0; r < _resources.Count; r++)
			{
				var resource = _resources[r];
				if (resource.Imported is not null || resource.First != i) continue;
				var physical = -1;
				for (var k = 0; k < _pool.Count; k++)
				{
					if (_pool[k].Descriptor == resource.Descriptor && _pool[k].BusyUntil < i) { physical = k; break; }
				}

				if (physical < 0)
				{
					_pool.Add(new Pooled { Descriptor = resource.Descriptor, Name = resource.Name, BusyUntil = -1 });
					physical = _pool.Count - 1;
				}

				var slot = _pool[physical];
				slot.BusyUntil = resource.Last;
				slot.LastUsedFrame = _frame;
				_pool[physical] = slot;
				resource.Physical = physical;
				_resources[r] = resource;
				_lifetimes.Add(new RenderGraphLifetime(resource.Name, resource.First, resource.Last, physical));
			}
		}

		_compiled = true;
	}

	/// <summary>
	/// Runs the compiled passes in order (compiling first if needed) on one command encoder of <paramref name="device"/>
	/// (created when a pass first asks for it, submitted after the last pass), allocating pooled textures on first use,
	/// then releases pooled textures unused for <see cref="UnusedFramesBeforeRelease"/> frames. Without a device the
	/// passes run with <see cref="RenderGraphContext.Device"/> null (tests).
	/// </summary>
	public void Execute(IGraphicsDevice? device)
	{
		if (!_compiled) Compile();
		_encoder.Device = device;
		try
		{
			var context = new RenderGraphContext(this, _encoder);
			foreach (var pass in _order) pass.Execute(context);
		}
		finally
		{
			_encoder.Submit();
			_encoder.Device = null;
		}

		ReleaseUnused();
	}

	private readonly EncoderHolder _encoder = new();

	/// <summary>The graph's current command encoder, created on demand.</summary>
	internal sealed class EncoderHolder
	{
		private ICommandEncoder? _current;

		public IGraphicsDevice? Device;

		public ICommandEncoder Get() => _current ??= (Device ?? throw new InvalidOperationException("The render graph runs without a device.")).CreateCommandEncoder("Ion render graph");

		public void Submit()
		{
			if (_current is null || Device is null) return;
			var encoder = _current;
			_current = null;
			Device.Queue.Submit(encoder.Finish());
		}
	}

	/// <summary>The texture behind <paramref name="texture"/>, allocating the pooled texture on first use.</summary>
	public ITexture GetTexture(RenderGraphTexture texture)
	{
		if (!texture.IsValid || texture.Index > _resources.Count) throw new ArgumentException("Invalid render graph texture.", nameof(texture));
		var resource = _resources[texture.Index - 1];
		if (resource.Imported is { } imported) return imported;
		if (resource.Physical < 0) throw new InvalidOperationException($"Render graph texture '{resource.Name}' is not used by any live pass.");
		var pooled = _pool[resource.Physical];
		if (pooled.Texture is null)
		{
			var allocate = _allocate ?? throw new InvalidOperationException("This render graph has no device to allocate textures on.");
			pooled.Texture = allocate(pooled.Descriptor, pooled.Name);
			_pool[resource.Physical] = pooled;
		}

		return pooled.Texture;
	}

	/// <summary>Releases pooled textures unused for <see cref="UnusedFramesBeforeRelease"/> frames.</summary>
	public void ReleaseUnused()
	{
		for (var i = _pool.Count - 1; i >= 0; i--)
		{
			if (_frame - _pool[i].LastUsedFrame < UnusedFramesBeforeRelease) continue;
			_pool[i].Texture?.Dispose();
			_pool.RemoveAt(i);
		}
	}

	/// <summary>Releases every pooled texture.</summary>
	public void Dispose()
	{
		foreach (var pooled in _pool) pooled.Texture?.Dispose();
		_pool.Clear();
	}

	private readonly record struct Access(int Pass, int Resource, bool Write);

	private struct Resource
	{
		public string Name;
		public ITexture? Imported;
		public bool Output;
		public RenderGraphTextureDescriptor Descriptor;
		public int First;
		public int Last;
		public int Physical;
	}

	private struct Pooled
	{
		public RenderGraphTextureDescriptor Descriptor;
		public string Name;
		public ITexture? Texture;
		public int BusyUntil;
		public long LastUsedFrame;
	}
}
