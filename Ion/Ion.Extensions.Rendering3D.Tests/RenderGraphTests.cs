using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering3D.Tests;

/// <summary>The render graph without a GPU: ordering, culling, validation, transient lifetimes and pooling.</summary>
public class RenderGraphTests
{
	private static readonly RenderGraphTextureDescriptor Color64 = new(64, 64, TextureFormat.Rgba8Unorm);
	private static readonly RenderGraphTextureDescriptor Depth64 = new(64, 64, TextureFormat.Depth32Float);

	/// <summary>A pass driven by delegates that records its execution.</summary>
	private sealed class TestPass(string name, int order, Action<RenderGraphBuilder> setup, List<string> log) : RenderGraphPass(name, order)
	{
		public override void Setup(RenderGraphBuilder builder) => setup(builder);

		public override void Execute(in RenderGraphContext context) => log.Add(Name);
	}

	/// <summary>Counts allocations and gives out textures that only have a size.</summary>
	private sealed class Allocator
	{
		public int Allocated;
		public int Disposed;

		public RenderGraph Graph() => new((descriptor, name) =>
		{
			Allocated++;
			return new FakeTexture(descriptor, this);
		});

		private sealed class FakeTexture(RenderGraphTextureDescriptor descriptor, Allocator owner) : ITexture
		{
			public TextureDimension Dimension => TextureDimension.D2;
			public uint Width => descriptor.Width;
			public uint Height => descriptor.Height;
			public TextureFormat Format => descriptor.Format;
			public TextureUsage Usage => descriptor.Usage;
			public uint MipLevelCount => 1;
			public uint SampleCount => 1;
			public ITextureView DefaultView => throw new NotSupportedException();
			public ITextureView CreateView(in TextureViewDescriptor descriptor) => throw new NotSupportedException();
			public void Dispose() => owner.Disposed++;
		}
	}

	private sealed class BackbufferTexture : ITexture
	{
		public TextureDimension Dimension => TextureDimension.D2;
		public uint Width => 64;
		public uint Height => 64;
		public TextureFormat Format => TextureFormat.Rgba8Unorm;
		public TextureUsage Usage => TextureUsage.RenderAttachment;
		public uint MipLevelCount => 1;
		public uint SampleCount => 1;
		public ITextureView DefaultView => throw new NotSupportedException();
		public ITextureView CreateView(in TextureViewDescriptor descriptor) => throw new NotSupportedException();
		public void Dispose() { }
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void PassesRunByOrderAndUnusedOnesAreCulled()
	{
		var log = new List<string>();
		var allocator = new Allocator();
		using var graph = allocator.Graph();
		graph.Reset();
		var backbuffer = graph.Import(RenderGraphResources.Backbuffer, new BackbufferTexture(), output: true);
		var shadow = graph.Import(RenderGraphResources.ShadowMap, new BackbufferTexture());
		RenderGraphTexture depth = default;

		// Added out of order: the graph sorts by Order (then insertion).
		graph.AddPass(new TestPass("Overlay", RenderGraphPass.Orders.Overlay, b => { b.ReadWrite(backbuffer); b.HasSideEffects(); }, log));
		graph.AddPass(new TestPass("Shadow", RenderGraphPass.Orders.Shadow, b => b.Write(shadow), log));
		graph.AddPass(new TestPass("Opaque", RenderGraphPass.Orders.Opaque, b =>
		{
			b.Write(backbuffer);
			depth = b.CreateTexture(RenderGraphResources.Depth, Depth64);
			b.Read(shadow);
		}, log));
		graph.AddPass(new TestPass("Skybox", RenderGraphPass.Orders.Skybox, b => { b.ReadWrite(backbuffer); b.Read(depth); }, log));
		// Writes a texture nobody reads: culled.
		graph.AddPass(new TestPass("Unused", RenderGraphPass.Orders.Post, b => b.CreateTexture("Scratch", Color64), log));
		graph.Compile();

		Assert.Equal(new[] { "Shadow", "Opaque", "Skybox", "Overlay" }, graph.ExecutionOrder.Select(p => p.Name));
		Assert.Equal(new[] { "Unused" }, graph.CulledPasses.Select(p => p.Name));
		graph.Execute(null);
		Assert.Equal(new[] { "Shadow", "Opaque", "Skybox", "Overlay" }, log);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AShadowPassNobodyReadsIsCulled()
	{
		var log = new List<string>();
		using var graph = new Allocator().Graph();
		graph.Reset();
		var backbuffer = graph.Import(RenderGraphResources.Backbuffer, new BackbufferTexture(), output: true);
		var shadow = graph.Import(RenderGraphResources.ShadowMap, new BackbufferTexture());
		graph.AddPass(new TestPass("Shadow", 100, b => b.Write(shadow), log));
		graph.AddPass(new TestPass("Unlit", 300, b => b.Write(backbuffer), log));
		graph.Compile();
		Assert.Equal(new[] { "Unlit" }, graph.ExecutionOrder.Select(p => p.Name));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void OverwritingATextureDropsTheEarlierWriter()
	{
		var log = new List<string>();
		using var graph = new Allocator().Graph();
		graph.Reset();
		var backbuffer = graph.Import(RenderGraphResources.Backbuffer, new BackbufferTexture(), output: true);
		graph.AddPass(new TestPass("First", 300, b => b.Write(backbuffer), log));
		graph.AddPass(new TestPass("Second", 400, b => b.Write(backbuffer), log));
		graph.AddPass(new TestPass("OnTop", 500, b => b.ReadWrite(backbuffer), log));
		graph.Compile();
		// Every pass that writes an output stays (First's result is overwritten, but it writes the output).
		Assert.Equal(new[] { "First", "Second", "OnTop" }, graph.ExecutionOrder.Select(p => p.Name));

		graph.Reset();
		var backbuffer2 = graph.Import(RenderGraphResources.Backbuffer, new BackbufferTexture(), output: true);
		RenderGraphTexture temp = default;
		graph.AddPass(new TestPass("TempA", 100, b => temp = b.CreateTexture("Temp", Color64), log));
		graph.AddPass(new TestPass("TempB", 200, b => b.Write(temp), log));
		graph.AddPass(new TestPass("Use", 300, b => { b.Read(temp); b.Write(backbuffer2); }, log));
		graph.Compile();
		// TempA's result is overwritten by TempB before anyone reads it.
		Assert.Equal(new[] { "TempB", "Use" }, graph.ExecutionOrder.Select(p => p.Name));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ReadingATransientNobodyWroteThrows()
	{
		using var graph = new Allocator().Graph();
		graph.Reset();
		var backbuffer = graph.Import(RenderGraphResources.Backbuffer, new BackbufferTexture(), output: true);
		var orphan = graph.CreateTexture("Orphan", Color64);
		graph.AddPass(new TestPass("Reader", 300, b => { b.Read(orphan); b.Write(backbuffer); }, []));
		var error = Assert.Throws<InvalidOperationException>(graph.Compile);
		Assert.Contains("'Orphan'", error.Message);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TransientTexturesWithDisjointLifetimesShareOnePooledTexture()
	{
		var allocator = new Allocator();
		using var graph = allocator.Graph();
		RenderGraphTexture a = default, b = default, c = default;
		ITexture? textureA = null, textureC = null;

		void Build()
		{
			graph.Reset();
			var backbuffer = graph.Import(RenderGraphResources.Backbuffer, new BackbufferTexture(), output: true);
			graph.AddPass(new Probe("WriteA", 100, builder => a = builder.CreateTexture("A", Color64), null));
			graph.AddPass(new Probe("ReadAWriteB", 200, builder => { builder.Read(a); b = builder.CreateTexture("B", Color64); }, (in RenderGraphContext context) => { textureA = context.GetTexture(a); context.GetTexture(b); }));
			// C starts after A's last use: it can reuse A's texture. B is still alive (read below), so C cannot take B's.
			graph.AddPass(new Probe("ReadBWriteC", 300, builder => { builder.Read(b); c = builder.CreateTexture("C", Color64); }, (in RenderGraphContext context) => textureC = context.GetTexture(c)));
			graph.AddPass(new Probe("Resolve", 400, builder => { builder.Read(c); builder.Write(backbuffer); }, null));
		}

		Build();
		graph.Compile();
		var lifetimes = graph.Lifetimes.ToDictionary(l => l.Name);
		Assert.Equal((0, 1), (lifetimes["A"].FirstPass, lifetimes["A"].LastPass));
		Assert.Equal((1, 2), (lifetimes["B"].FirstPass, lifetimes["B"].LastPass));
		Assert.Equal((2, 3), (lifetimes["C"].FirstPass, lifetimes["C"].LastPass));
		Assert.Equal(lifetimes["A"].PhysicalTexture, lifetimes["C"].PhysicalTexture);
		Assert.NotEqual(lifetimes["A"].PhysicalTexture, lifetimes["B"].PhysicalTexture);
		Assert.Equal(2, graph.PooledTextureCount);

		graph.Execute(null);
		Assert.Same(textureA, textureC);
		Assert.Equal(2, allocator.Allocated);

		// The next frames reuse the pooled textures.
		for (var i = 0; i < 3; i++)
		{
			Build();
			graph.Execute(null);
		}

		Assert.Equal(2, allocator.Allocated);

		// Textures unused for UnusedFramesBeforeRelease frames are released.
		for (var i = 0; i < graph.UnusedFramesBeforeRelease; i++)
		{
			graph.Reset();
			graph.Execute(null);
		}

		Assert.Equal(0, graph.PooledTextureCount);
		Assert.Equal(2, allocator.Disposed);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DifferentDescriptorsNeverAlias()
	{
		using var graph = new Allocator().Graph();
		graph.Reset();
		var backbuffer = graph.Import(RenderGraphResources.Backbuffer, new BackbufferTexture(), output: true);
		RenderGraphTexture color = default, depth = default;
		graph.AddPass(new Probe("Color", 100, b => color = b.CreateTexture("Color", Color64), null));
		graph.AddPass(new Probe("Blit", 200, b => { b.Read(color); b.Write(backbuffer); }, null));
		graph.AddPass(new Probe("Depth", 300, b => { depth = b.CreateTexture("Depth", Depth64); b.Write(backbuffer); }, null));
		graph.Compile();
		var lifetimes = graph.Lifetimes.ToDictionary(l => l.Name);
		Assert.NotEqual(lifetimes["Color"].PhysicalTexture, lifetimes["Depth"].PhysicalTexture);
	}

	private sealed class Probe(string name, int order, Action<RenderGraphBuilder> setup, RenderGraphExecute? execute) : RenderGraphPass(name, order)
	{
		public override void Setup(RenderGraphBuilder builder) => setup(builder);

		public override void Execute(in RenderGraphContext context) => execute?.Invoke(context);
	}

	private delegate void RenderGraphExecute(in RenderGraphContext context);
}
