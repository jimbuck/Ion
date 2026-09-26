using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering3D;

/// <summary>The built-in passes, one set per camera slot, reused every frame.</summary>
internal sealed class PassSet
{
	public PassSet(Renderer3D renderer, int views)
	{
		Shadow = new ShadowPass(renderer);
		Overlay = new Overlay2DPass(renderer);
		Prepass = new DepthPrepass[views];
		Opaque = new OpaquePass[views];
		Skybox = new SkyboxPass[views];
		Transparent = new TransparentPass[views];
		DepthNames = new string[views];
		for (var v = 0; v < views; v++)
		{
			Prepass[v] = new DepthPrepass(renderer, v);
			Opaque[v] = new OpaquePass(renderer, v);
			Skybox[v] = new SkyboxPass(renderer, v);
			Transparent[v] = new TransparentPass(renderer, v);
			DepthNames[v] = $"Depth.{v}";
		}
	}

	public ShadowPass Shadow { get; }

	public Overlay2DPass Overlay { get; }

	public DepthPrepass[] Prepass { get; }

	public OpaquePass[] Opaque { get; }

	public SkyboxPass[] Skybox { get; }

	public TransparentPass[] Transparent { get; }

	public string[] DepthNames { get; }

	/// <summary>The frame slot's instance buffer (vertex slot 1 of every mesh draw).</summary>
	public IBuffer? Instances { get; set; }

	/// <summary>
	/// Draws batches: pipeline, material and mesh bindings change only when they differ from the previous batch. Batches
	/// whose material samples <paramref name="target"/> (the texture of the render target being drawn into) are skipped:
	/// a texture cannot be sampled while it is an attachment.
	/// </summary>
	public static void DrawBatches(Renderer3D renderer, IRenderPassEncoder pass, ReadOnlySpan<Batch> batches, IBuffer instances, TextureFormat format, bool afterPrepass, TextureHandle target)
	{
		IRenderPipeline? pipeline = null;
		var material = -1;
		var mesh = -1;
		MeshSlot? meshSlot = null;
		foreach (ref readonly var batch in batches)
		{
			var materialSlot = renderer.Material(batch.Material);
			if (target.IsValid && Array.IndexOf(materialSlot.Textures, target) >= 0) continue;
			var next = renderer.MainPipeline(materialSlot, format, afterPrepass);
			if (!ReferenceEquals(next, pipeline))
			{
				pass.SetPipeline(next);
				pipeline = next;
			}

			if (batch.Material != material)
			{
				pass.SetBindGroup(1, materialSlot.BindGroup!);
				material = batch.Material;
			}

			if (batch.Mesh != mesh)
			{
				meshSlot = renderer.Mesh(batch.Mesh)!;
				pass.SetVertexBuffer(0, meshSlot.Vertices!);
				pass.SetIndexBuffer(meshSlot.Indices!, IndexFormat.Uint32);
				mesh = batch.Mesh;
			}

			pass.SetVertexBuffer(1, instances, (ulong)batch.FirstInstance * InstanceData.Size);
			foreach (var sub in meshSlot!.SubMeshes)
			{
				pass.DrawIndexed((uint)sub.IndexCount, (uint)batch.InstanceCount, (uint)sub.FirstIndex, sub.BaseVertex);
			}
		}
	}

	/// <summary>Draws batches with the depth-only pipeline already set (materials ignored; only opaque ones when <paramref name="opaqueOnly"/>).</summary>
	public static void DrawDepthBatches(Renderer3D renderer, IRenderPassEncoder pass, ReadOnlySpan<Batch> batches, IBuffer instances, bool opaqueOnly)
	{
		var mesh = -1;
		MeshSlot? meshSlot = null;
		foreach (ref readonly var batch in batches)
		{
			if (opaqueOnly && renderer.Material(batch.Material).AlphaMode != AlphaMode.Opaque) continue;
			if (batch.Mesh != mesh)
			{
				meshSlot = renderer.Mesh(batch.Mesh)!;
				pass.SetVertexBuffer(0, meshSlot.Vertices!);
				pass.SetIndexBuffer(meshSlot.Indices!, IndexFormat.Uint32);
				mesh = batch.Mesh;
			}

			pass.SetVertexBuffer(1, instances, (ulong)batch.FirstInstance * InstanceData.Size);
			foreach (var sub in meshSlot!.SubMeshes)
			{
				pass.DrawIndexed((uint)sub.IndexCount, (uint)batch.InstanceCount, (uint)sub.FirstIndex, sub.BaseVertex);
			}
		}
	}

	/// <summary>Sets the view's viewport and scissor.</summary>
	public static void SetViewport(IRenderPassEncoder pass, ViewData view)
	{
		pass.SetViewport(view.ViewportX, view.ViewportY, view.ViewportWidth, view.ViewportHeight);
		pass.SetScissorRect((uint)view.ViewportX, (uint)view.ViewportY, (uint)view.ViewportWidth, (uint)view.ViewportHeight);
	}
}

/// <summary>The directional light's shadow map: every caster batch, depth only.</summary>
internal sealed class ShadowPass(Renderer3D renderer) : RenderGraphPass("Shadow", Orders.Shadow)
{
	public RenderGraphTexture Target;

	public override void Setup(RenderGraphBuilder builder) => builder.Write(Target);

	public override void Execute(in RenderGraphContext context)
	{
		var target = context.GetTexture(Target);
		var pass = context.Encoder.BeginRenderPass(new RenderPassDescriptor([], new RenderPassDepthStencilAttachment(target.DefaultView, LoadOp.Clear, StoreOp.Store, 1f), "Ion 3D shadow"));
		pass.SetPipeline(renderer.DepthPipeline(shadow: true));
		pass.SetBindGroup(0, renderer.ShadowGroup());
		PassSet.DrawDepthBatches(renderer, pass, renderer.ShadowBatches, renderer.PassInstances, opaqueOnly: false);
		pass.End();
	}
}

/// <summary>The optional depth prepass of one camera: opaque batches into its depth buffer.</summary>
internal sealed class DepthPrepass(Renderer3D renderer, int view) : RenderGraphPass($"DepthPrepass.{view}", Orders.DepthPrepass)
{
	private RenderGraphTexture _depth;

	public void Configure(RenderGraphTexture depth, RenderGraphTexture color) => _depth = depth;

	public override void Setup(RenderGraphBuilder builder) => builder.Write(_depth);

	public override void Execute(in RenderGraphContext context)
	{
		var data = renderer.View(view);
		var depth = context.GetTexture(_depth);
		var pass = context.Encoder.BeginRenderPass(new RenderPassDescriptor([], new RenderPassDepthStencilAttachment(depth.DefaultView, LoadOp.Clear, StoreOp.Store, 1f), "Ion 3D depth prepass"));
		PassSet.SetViewport(pass, data);
		pass.SetPipeline(renderer.DepthPipeline(shadow: false));
		pass.SetBindGroup(0, renderer.ViewGroup(view));
		PassSet.DrawDepthBatches(renderer, pass, data.Opaque.Span, renderer.PassInstances, opaqueOnly: true);
		pass.End();
	}
}

/// <summary>
/// The opaque pass of one camera: clears (or loads) its target, draws the clear triangle for a camera sharing its
/// target, then the opaque and masked batches front to back.
/// </summary>
internal sealed class OpaquePass(Renderer3D renderer, int view) : RenderGraphPass($"Opaque.{view}", Orders.Opaque)
{
	private RenderGraphTexture _color;
	private RenderGraphTexture _depth;
	private RenderGraphTexture _shadow;
	private bool _afterPrepass;

	public void Configure(RenderGraphTexture color, RenderGraphTexture depth, RenderGraphTexture shadow, bool afterPrepass)
	{
		_color = color;
		_depth = depth;
		_shadow = shadow;
		_afterPrepass = afterPrepass;
	}

	public override void Setup(RenderGraphBuilder builder)
	{
		var data = renderer.View(view);
		if (data.FirstOnTarget && data.Camera.Clear != CameraClear.None) builder.Write(_color);
		else builder.ReadWrite(_color);
		if (_afterPrepass) builder.ReadWrite(_depth);
		else builder.Write(_depth);
		if (renderer.ShadowActive) builder.Read(_shadow);
	}

	public override void Execute(in RenderGraphContext context)
	{
		var data = renderer.View(view);
		var color = context.GetTexture(_color);
		var depth = context.GetTexture(_depth);
		var colorAttachment = ColorAttachment(renderer, data, color);
		var depthAttachment = new RenderPassDepthStencilAttachment(depth.DefaultView, _afterPrepass ? LoadOp.Load : LoadOp.Clear, StoreOp.Store, 1f);
		var pass = context.Encoder.BeginRenderPass(new RenderPassDescriptor([colorAttachment], depthAttachment, "Ion 3D opaque"));
		PassSet.SetViewport(pass, data);
		pass.SetBindGroup(0, renderer.ViewGroup(view));
		if (data.DrawClearQuad)
		{
			pass.SetPipeline(renderer.BackgroundPipeline(color.Format, clearQuad: true));
			pass.Draw(3);
		}

		PassSet.DrawBatches(renderer, pass, data.Opaque.Span, renderer.PassInstances, color.Format, _afterPrepass, renderer.TargetTexture(data));
		pass.End();
	}

	/// <summary>
	/// The color attachment of a camera's first pass: the frame's own first-use attachment for the frame target (so the
	/// 2D overlay loads what 3D drew), cleared to the camera's clear color when it is the first camera on its target.
	/// </summary>
	private static RenderPassColorAttachment ColorAttachment(Renderer3D renderer, ViewData data, ITexture color)
	{
		var clear = data.FirstOnTarget && data.Camera.Clear != CameraClear.None;
		var value = Renderer3D.ClearValue(data.Camera.ClearColor, color.Format);
		if (data.Target == 0)
		{
			var attachment = renderer.Frame.ColorAttachment();
			if (clear) return attachment with { LoadOp = LoadOp.Clear, ClearValue = value };
			return data.FirstOnTarget ? attachment : attachment with { LoadOp = LoadOp.Load };
		}

		return new RenderPassColorAttachment(color.DefaultView, clear ? LoadOp.Clear : LoadOp.Load, StoreOp.Store, value);
	}
}

/// <summary>The skybox of one camera: the environment cube map where nothing was drawn (depth 1).</summary>
internal sealed class SkyboxPass(Renderer3D renderer, int view) : RenderGraphPass($"Skybox.{view}", Orders.Skybox)
{
	private RenderGraphTexture _color;
	private RenderGraphTexture _depth;

	public void Configure(RenderGraphTexture color, RenderGraphTexture depth)
	{
		_color = color;
		_depth = depth;
	}

	public override void Setup(RenderGraphBuilder builder)
	{
		builder.ReadWrite(_color);
		builder.Read(_depth);
	}

	public override void Execute(in RenderGraphContext context)
	{
		var data = renderer.View(view);
		var color = context.GetTexture(_color);
		var depth = context.GetTexture(_depth);
		var pass = context.Encoder.BeginRenderPass(new RenderPassDescriptor(
			[new RenderPassColorAttachment(color.DefaultView, LoadOp.Load, StoreOp.Store)],
			new RenderPassDepthStencilAttachment(depth.DefaultView, LoadOp.Load, StoreOp.Store, 1f), "Ion 3D skybox"));
		PassSet.SetViewport(pass, data);
		pass.SetPipeline(renderer.BackgroundPipeline(color.Format, clearQuad: false));
		pass.SetBindGroup(0, renderer.ViewGroup(view));
		pass.Draw(3);
		pass.End();
	}
}

/// <summary>The transparent pass of one camera: blended batches back to front, depth tested but not written.</summary>
internal sealed class TransparentPass(Renderer3D renderer, int view) : RenderGraphPass($"Transparent.{view}", Orders.Transparent)
{
	private RenderGraphTexture _color;
	private RenderGraphTexture _depth;
	private RenderGraphTexture _shadow;

	public void Configure(RenderGraphTexture color, RenderGraphTexture depth, RenderGraphTexture shadow)
	{
		_color = color;
		_depth = depth;
		_shadow = shadow;
	}

	public override void Setup(RenderGraphBuilder builder)
	{
		builder.ReadWrite(_color);
		builder.Read(_depth);
		if (renderer.ShadowActive) builder.Read(_shadow);
	}

	public override void Execute(in RenderGraphContext context)
	{
		var data = renderer.View(view);
		var color = context.GetTexture(_color);
		var depth = context.GetTexture(_depth);
		var pass = context.Encoder.BeginRenderPass(new RenderPassDescriptor(
			[new RenderPassColorAttachment(color.DefaultView, LoadOp.Load, StoreOp.Store)],
			new RenderPassDepthStencilAttachment(depth.DefaultView, LoadOp.Load, StoreOp.Discard, 1f), "Ion 3D transparent"));
		PassSet.SetViewport(pass, data);
		pass.SetBindGroup(0, renderer.ViewGroup(view));
		PassSet.DrawBatches(renderer, pass, data.Transparent.Span, renderer.PassInstances, color.Format, afterPrepass: false, renderer.TargetTexture(data));
		pass.End();
	}
}

/// <summary>
/// The 2D overlay: submits the 3D commands recorded so far, then the sprite batch's frame (its submission is deferred to
/// this pass), so sprites and text draw on top of the 3D scene.
/// </summary>
internal sealed class Overlay2DPass(Renderer3D renderer) : RenderGraphPass("Overlay2D", Orders.Overlay)
{
	public RenderGraphTexture Target;

	public override void Setup(RenderGraphBuilder builder)
	{
		builder.ReadWrite(Target);
		builder.HasSideEffects();
	}

	public override void Execute(in RenderGraphContext context)
	{
		context.Flush();
		renderer.Overlay?.SubmitDeferred();
	}
}
