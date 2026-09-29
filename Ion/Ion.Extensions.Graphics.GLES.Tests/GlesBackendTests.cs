using System.Runtime.InteropServices;
using System.Text;

using Silk.NET.OpenGLES;
using Silk.NET.Shaderc;

using Ion.Examples.Quad;
using Ion.Extensions.Graphics.Rhi;
using Ion.Extensions.Graphics.Rhi.Tests;
using Ion.Shaders;

namespace Ion.Extensions.Graphics.GLES.Tests;

/// <summary>Compiles GLSL 4.5 test shaders the way the build does (Shaderc, then SPIRV-Cross to GLSL ES) and loads them.</summary>
internal static class TestShaders
{
	public static string ToGlslEs(string source, string name) =>
		ShaderCompiler.TranslateToGlslEs(ShaderCompiler.CompileToSpirV(source, ShaderCompiler.KindOf(name), name));

	public static IShaderModule Load(IGraphicsDevice device, string source, string name)
	{
		var stage = name.EndsWith(".vert", StringComparison.Ordinal) ? ShaderStage.Vertex : ShaderStage.Fragment;
		return device.CreateShaderModule(new ShaderModuleDescriptor(Encoding.UTF8.GetBytes(ToGlslEs(source, name)), stage, ShaderLanguage.GlslEs, name));
	}

	/// <summary>A full-target triangle from gl_VertexIndex, no vertex buffers.</summary>
	public const string FullScreenVertex = """
		#version 450
		void main()
		{
			vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
			gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
		}
		""";
}

/// <summary>A GLES test at every feature level (ES 3.0 fallback, ES 3.1, the driver's ES 3.2); skipped without EGL.</summary>
public sealed class GlesLevelsDataAttribute : Xunit.Sdk.DataAttribute
{
	/// <summary>Skips the rows when EGL cannot create an OpenGL ES 3 context.</summary>
	public GlesLevelsDataAttribute()
	{
		if (TestEnvironment.SkipReason(GraphicsBackend.OpenGLES, windowed: false) is { } reason) Skip = reason;
	}

	/// <inheritdoc/>
	public override IEnumerable<object[]> GetData(System.Reflection.MethodInfo testMethod) =>
		[[GlesFeatureLevel.Es30], [GlesFeatureLevel.Es31], [GlesFeatureLevel.Es32]];
}

/// <summary>
/// The binding-point scheme shared by the shader tool and the backend (<see cref="GlesBindings"/>): (group, binding)
/// flattened to uniform block binding points and texture units, and the flattening table.
/// </summary>
public class GlesBindingSchemeTests
{
	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(0u, 0u, 0)]
	[InlineData(0u, 7u, 7)]
	[InlineData(1u, 0u, 8)]
	[InlineData(2u, 3u, 19)]
	[InlineData(3u, 7u, 31)]
	public void SlotsAreGroupTimesEightPlusBinding(uint group, uint binding, int slot) => Assert.Equal(slot, GlesBindings.Slot(group, binding));

	[Fact, Trait(CATEGORY, UNIT)]
	public void SlotsOutsideTheSchemeThrow()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => GlesBindings.Slot(4, 0));
		Assert.Throws<ArgumentOutOfRangeException>(() => GlesBindings.Slot(0, 8));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheTableRoundTrips()
	{
		var source = "#version 310 es\nvoid main() { }\n// a comment\n"
			+ GlesBindings.FormatBlock("ubo", "Camera", 1, 2) + "\n"
			+ GlesBindings.FormatSampler("uAtlas_uPoint", 2, 0, 2, 1) + "\n";
		var table = GlesBindings.ParseTable(source);
		Assert.Equal(
		[
			new GlesBindingEntry(GlesBindingKind.UniformBlock, "Camera", 1, 2, 10),
			new GlesBindingEntry(GlesBindingKind.Sampler, "uAtlas_uPoint", 2, 0, 16, 2, 1),
		], table);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheBuildEmbedsFlattenedQuadShaders()
	{
		var assembly = typeof(TexturedQuad).Assembly;
		var fragment = Encoding.UTF8.GetString(EmbeddedShaders.Load(assembly, "textured_quad.frag", ShaderLanguage.GlslEs));
		Assert.Contains("layout(binding = 1) uniform highp sampler2D uTexture_uSampler;", fragment);
		Assert.Contains("// ion:sampler uTexture_uSampler set=0 binding=1 sampler-set=0 sampler-binding=2 slot=1", fragment);

		var vertex = Encoding.UTF8.GetString(EmbeddedShaders.Load(assembly, "textured_quad.vert", ShaderLanguage.GlslEs));
		Assert.Contains("layout(binding = 0, std140) uniform Transform", vertex);
		Assert.Contains("// ion:ubo Transform set=0 binding=0 slot=0", vertex);
		// WebGPU clip space: y negated (texture row 0 at the top) and depth remapped from [0, 1].
		Assert.Contains("gl_Position.y = -gl_Position.y;", vertex);
		Assert.Contains("gl_Position.z = 2.0 * gl_Position.z - gl_Position.w;", vertex);
		// Varyings named by location, so ES 3.00 stages link by name.
		Assert.Contains("out vec2 ion_var_0;", vertex);
		Assert.Contains("in highp vec2 ion_var_0;", fragment);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheShaderToolFlattensEveryGroup()
	{
		var glsl = TestShaders.ToGlslEs(MultiGroupShaders.Fragment, "multi.frag");
		Assert.Contains("layout(binding = 0, std140) uniform Tint", glsl);
		Assert.Contains("layout(binding = 11) uniform highp sampler2D uTexture_uSampler", glsl);
		Assert.Contains("// ion:sampler uTexture_uSampler set=1 binding=3 sampler-set=1 sampler-binding=5 slot=11", glsl);

		var vertex = TestShaders.ToGlslEs(MultiGroupShaders.Vertex, "multi.vert");
		Assert.Contains("layout(binding = 17, std140) uniform Offset", vertex);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void OneTextureSampledWithTwoSamplersIsABuildError()
	{
		const string source = """
			#version 450
			layout(location = 0) out vec4 color;
			layout(set = 0, binding = 0) uniform texture2D tex;
			layout(set = 0, binding = 1) uniform sampler a;
			layout(set = 0, binding = 2) uniform sampler b;
			void main() { color = texture(sampler2D(tex, a), vec2(0.0)) + texture(sampler2D(tex, b), vec2(1.0)); }
			""";
		var ex = Assert.Throws<ShaderCompilationException>(() => TestShaders.ToGlslEs(source, "two.frag"));
		Assert.Contains("one sampler", ex.Message);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheEs30FallbackRewritesShadersToGlslEs300()
	{
		var assembly = typeof(TexturedQuad).Assembly;
		var vertex = GlesShaderModule.ToGlslEs300(Encoding.UTF8.GetString(EmbeddedShaders.Load(assembly, "textured_quad.vert", ShaderLanguage.GlslEs)), ShaderStage.Vertex);
		Assert.StartsWith("#version 300 es", vertex);
		Assert.Contains("layout(std140) uniform Transform", vertex);
		Assert.DoesNotContain("binding =", vertex.Split("// Ion GLES binding table")[0]);
		Assert.Contains("\nout vec2 ion_var_0;", vertex);
		Assert.Contains("layout(location = 1) in vec2 inUv;", vertex);

		var fragment = GlesShaderModule.ToGlslEs300(Encoding.UTF8.GetString(EmbeddedShaders.Load(assembly, "textured_quad.frag", ShaderLanguage.GlslEs)), ShaderStage.Fragment);
		Assert.Contains("\nuniform highp sampler2D uTexture_uSampler;", fragment);
		Assert.Contains("\nin highp vec2 ion_var_0;", fragment);
		Assert.Contains("layout(location = 0) out highp vec4 outColor;", fragment);
	}
}

internal static class MultiGroupShaders
{
	public const string Vertex = """
		#version 450
		layout(location = 0) in vec2 inPosition;
		layout(location = 1) in vec2 inUv;
		layout(location = 0) out vec2 vUv;
		layout(set = 2, binding = 1) uniform Offset { vec4 offset; };
		void main()
		{
			vUv = inUv;
			gl_Position = vec4(inPosition + offset.xy, 0.0, 1.0);
		}
		""";

	public const string Fragment = """
		#version 450
		layout(location = 0) in vec2 vUv;
		layout(location = 0) out vec4 outColor;
		layout(set = 0, binding = 0) uniform Tint { vec4 tint; };
		layout(set = 1, binding = 3) uniform texture2D uTexture;
		layout(set = 1, binding = 5) uniform sampler uSampler;
		void main()
		{
			outColor = texture(sampler2D(uTexture, uSampler), vUv) * tint;
		}
		""";
}

/// <summary>
/// The GLES backend itself: feature levels and their fallbacks, bind groups on flattened slots, std140 uniform blocks and
/// offsets, sampler objects, frames in flight on fence syncs, readback conversions.
/// </summary>
public class GlesDeviceTests
{
	private static readonly Vector4 Black = new(0, 0, 0, 1);

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void TheFeatureLevelIsTheLowerOfTheContextAndTheCap()
	{
		var log = new ValidationLog();
		using (var device = (GlesDevice)log.CreateDevice(GraphicsBackend.OpenGLES))
		{
			Assert.True(device.Version >= new Version(3, 0));
			var expected = device.Version >= new Version(3, 2) ? GlesFeatureLevel.Es32 : device.Version >= new Version(3, 1) ? GlesFeatureLevel.Es31 : GlesFeatureLevel.Es30;
			Assert.Equal(expected, device.FeatureLevel);
			Assert.Equal(ShaderLanguage.GlslEs, device.ShaderLanguage);
			Assert.True(device.Limits.MaxBindGroups >= 3);
			Assert.True(device.MaxUniformBufferBindings >= 24);
			Assert.True(device.MaxTextureUnits >= 32);
		}

		using (var es30 = (GlesDevice)log.CreateDevice(GraphicsBackend.OpenGLES, glesLevel: GlesFeatureLevel.Es30))
		{
			Assert.Equal(GlesFeatureLevel.Es30, es30.FeatureLevel);
			Assert.False(es30.VertexAttribBinding);
			Assert.False(es30.NativeBaseVertex);
			Assert.Throws<NotSupportedException>(() => es30.CreateBuffer(new BufferDescriptor(16, BufferUsage.Storage)));
		}

		using (var es31 = (GlesDevice)log.CreateDevice(GraphicsBackend.OpenGLES, glesLevel: GlesFeatureLevel.Es31))
		{
			Assert.Equal(GlesFeatureLevel.Es31, es31.FeatureLevel);
			Assert.True(es31.VertexAttribBinding);
			Assert.False(es31.NativeBaseVertex);
		}

		log.AssertClean();
	}

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void TheGlesBackendTakesOnlyGlslEs()
	{
		using var device = GlesDevice.Create(new GlesDeviceOptions());
		var spirv = EmbeddedShaders.Load(typeof(TexturedQuad).Assembly, "textured_quad.vert", ShaderLanguage.SpirV);
		Assert.Throws<NotSupportedException>(() => device.CreateShaderModule(new ShaderModuleDescriptor(spirv, ShaderStage.Vertex, ShaderLanguage.SpirV)));
		Assert.Throws<ArgumentException>(() => device.CreateShaderModule(new ShaderModuleDescriptor("void main() {}"u8.ToArray(), ShaderStage.Vertex, ShaderLanguage.GlslEs)));
		var ex = Assert.Throws<InvalidOperationException>(() => device.CreateShaderModule(new ShaderModuleDescriptor("#version 310 es\nvoid main() { undefined(); }\n"u8.ToArray(), ShaderStage.Vertex, ShaderLanguage.GlslEs, "broken.vert")));
		Assert.Contains("broken.vert", ex.Message);
	}

	[Theory, GlesLevelsData, Trait(CATEGORY, INTEGRATION)]
	public void BindGroupsBindToTheirFlattenedSlots(GlesFeatureLevel level)
	{
		// Three groups: a fragment uniform block at (0, 0), a texture and sampler at (1, 3) and (1, 5), a vertex uniform
		// block at (2, 1): binding points 0 and 17, texture unit 11.
		var log = new ValidationLog();
		Screenshot shot;
		using (var device = log.CreateDevice(GraphicsBackend.OpenGLES, glesLevel: level))
		{
			using var vertex = TestShaders.Load(device, MultiGroupShaders.Vertex, "multi.vert");
			using var fragment = TestShaders.Load(device, MultiGroupShaders.Fragment, "multi.frag");
			using var tintLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor([new(0, ShaderStage.Fragment, BindingType.UniformBuffer)]));
			using var textureLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor([new(3, ShaderStage.Fragment, BindingType.Texture), new(5, ShaderStage.Fragment, BindingType.Sampler)]));
			using var offsetLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor([new(1, ShaderStage.Vertex, BindingType.UniformBuffer)]));
			using var pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([tintLayout, textureLayout, offsetLayout]));
			using var pipeline = device.CreateRenderPipeline(new RenderPipelineDescriptor
			{
				Layout = pipelineLayout,
				Vertex = new VertexState(vertex, [new VertexBufferLayout(16, VertexStepMode.Vertex, [new(VertexFormat.Float32x2, 0, 0), new(VertexFormat.Float32x2, 8, 1)])]),
				Fragment = new FragmentState(fragment, [new ColorTargetState(TextureFormat.Rgba8Unorm)]),
			});

			using var tint = device.CreateBuffer(new BufferDescriptor(16, BufferUsage.Uniform | BufferUsage.CopyDst));
			device.Queue.WriteBuffer(tint, 0, new Vector4(0.5f, 1, 1, 1));
			using var offset = device.CreateBuffer(new BufferDescriptor(16, BufferUsage.Uniform | BufferUsage.CopyDst));
			device.Queue.WriteBuffer(offset, 0, new Vector4(0.5f, 0, 0, 0));
			using var texture = device.CreateTexture(new TextureDescriptor(1, 1, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst));
			device.Queue.WriteTexture(texture, [200, 100, 50, 255]);
			using var sampler = device.CreateSampler(SamplerDescriptor.PointClamp);
			using var tintGroup = device.CreateBindGroup(new BindGroupDescriptor(tintLayout, [BindGroupEntry.ForBuffer(0, tint)]));
			using var textureGroup = device.CreateBindGroup(new BindGroupDescriptor(textureLayout, [BindGroupEntry.ForTexture(3, texture.DefaultView), BindGroupEntry.ForSampler(5, sampler)]));
			using var offsetGroup = device.CreateBindGroup(new BindGroupDescriptor(offsetLayout, [BindGroupEntry.ForBuffer(1, offset)]));

			using var vertices = device.CreateBuffer(new BufferDescriptor(64, BufferUsage.Vertex | BufferUsage.CopyDst));
			device.Queue.WriteBuffer(vertices, 0, (ReadOnlySpan<float>)[-1, 1, 0, 0, 1, 1, 1, 0, 1, -1, 1, 1, -1, -1, 0, 1]);
			using var indices = device.CreateBuffer(new BufferDescriptor(12, BufferUsage.Index | BufferUsage.CopyDst));
			device.Queue.WriteBuffer(indices, 0, (ReadOnlySpan<ushort>)[0, 1, 2, 0, 2, 3]);
			using var target = device.CreateTexture(new TextureDescriptor(16, 16, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));

			var encoder = device.CreateCommandEncoder();
			var pass = encoder.BeginRenderPass(new RenderPassDescriptor([new RenderPassColorAttachment(target.DefaultView, LoadOp.Clear, StoreOp.Store, Black)]));
			pass.SetPipeline(pipeline);
			pass.SetBindGroup(0, tintGroup);
			pass.SetBindGroup(1, textureGroup);
			pass.SetBindGroup(2, offsetGroup);
			pass.SetVertexBuffer(0, vertices);
			pass.SetIndexBuffer(indices, IndexFormat.Uint16);
			pass.DrawIndexed(6);
			pass.End();
			device.Queue.Submit(encoder.Finish());
			shot = Readback.Read(device, target);
		}

		log.AssertClean();
		// Shifted right by a quarter of the target: the left quarter keeps the clear color.
		QuadAssert.AssertPixel(shot, 1, 8, new Rgba8(0, 0, 0, 255));
		QuadAssert.AssertPixel(shot, 10, 8, new Rgba8(100, 100, 50, 255));
	}

	[Theory, GlesLevelsData, Trait(CATEGORY, INTEGRATION)]
	public void UniformBlocksUseTheStd140LayoutAndHonourBindingOffsets(GlesFeatureLevel level)
	{
		const string fragmentSource = """
			#version 450
			layout(location = 0) out vec4 outColor;
			layout(set = 0, binding = 0) uniform Block { vec3 a; float b; vec2 c; float arr[2]; vec4 d; };
			layout(set = 0, binding = 1) uniform Select { vec4 which; };
			void main() { outColor = which.x > 0.5 ? d : vec4(a.z, b, c.y, arr[1]); }
			""";

		var log = new ValidationLog();
		var shots = new List<Screenshot>();
		using (var device = log.CreateDevice(GraphicsBackend.OpenGLES, glesLevel: level))
		{
			var alignment = device.Limits.MinUniformBufferOffsetAlignment;
			Assert.True(alignment is > 0 and <= 256 && (alignment & (alignment - 1)) == 0, $"Alignment {alignment} is not a power of two up to 256.");

			using var vertex = TestShaders.Load(device, TestShaders.FullScreenVertex, "full.vert");
			using var fragment = TestShaders.Load(device, fragmentSource, "std140.frag");
			using var layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor([new(0, ShaderStage.Fragment, BindingType.UniformBuffer), new(1, ShaderStage.Fragment, BindingType.UniformBuffer)]));
			using var pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([layout]));
			using var pipeline = device.CreateRenderPipeline(new RenderPipelineDescriptor
			{
				Layout = pipelineLayout,
				Vertex = new VertexState(vertex, []),
				Fragment = new FragmentState(fragment, [new ColorTargetState(TextureFormat.Rgba8Unorm)]),
			});

			// std140: a at 0, b at 12, c at 16, arr[0] at 32 and arr[1] at 48 (array stride 16), d at 64; 80 bytes.
			static byte[] Block(float az, float b, float cy, float arr1, Vector4 d)
			{
				var bytes = new byte[80];
				var floats = MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
				floats[2] = az;
				floats[3] = b;
				floats[5] = cy;
				floats[12] = arr1;
				floats[16] = d.X;
				floats[17] = d.Y;
				floats[18] = d.Z;
				floats[19] = d.W;
				return bytes;
			}

			// The second block at the first aligned offset past the first one.
			var second = (80 + alignment - 1) / alignment * alignment;
			using var blocks = device.CreateBuffer(new BufferDescriptor(second + 80, BufferUsage.Uniform | BufferUsage.CopyDst));
			device.Queue.WriteBuffer(blocks, 0, Block(0.2f, 0.4f, 0.6f, 0.8f, new Vector4(1, 0.5f, 0.25f, 1)));
			device.Queue.WriteBuffer(blocks, second, Block(0.8f, 0.6f, 0.4f, 0.2f, new Vector4(0, 1, 0, 1)));
			using var selectMembers = device.CreateBuffer(new BufferDescriptor(16, BufferUsage.Uniform | BufferUsage.CopyDst));
			using var selectD = device.CreateBuffer(new BufferDescriptor(16, BufferUsage.Uniform | BufferUsage.CopyDst));
			device.Queue.WriteBuffer(selectMembers, 0, Vector4.Zero);
			device.Queue.WriteBuffer(selectD, 0, Vector4.One);
			using var target = device.CreateTexture(new TextureDescriptor(4, 4, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));

			foreach (var (offset, select) in new[] { (0ul, selectMembers), (0ul, selectD), (second, selectMembers) })
			{
				using var group = device.CreateBindGroup(new BindGroupDescriptor(layout, [BindGroupEntry.ForBuffer(0, blocks, offset, 80), BindGroupEntry.ForBuffer(1, select)]));
				var encoder = device.CreateCommandEncoder();
				var pass = encoder.BeginRenderPass(new RenderPassDescriptor([new RenderPassColorAttachment(target.DefaultView, LoadOp.Clear, StoreOp.Store, Black)]));
				pass.SetPipeline(pipeline);
				pass.SetBindGroup(0, group);
				pass.Draw(3);
				pass.End();
				device.Queue.Submit(encoder.Finish());
				shots.Add(Readback.Read(device, target));
			}

			// A misaligned offset is rejected when the bind group is created.
			if (alignment > 1) Assert.Throws<ArgumentException>(() => device.CreateBindGroup(new BindGroupDescriptor(layout, [BindGroupEntry.ForBuffer(0, blocks, alignment / 2, 64), BindGroupEntry.ForBuffer(1, selectD)])));
		}

		log.AssertClean();
		QuadAssert.AssertPixel(shots[0], 2, 2, new Rgba8(51, 102, 153, 204));
		QuadAssert.AssertPixel(shots[1], 2, 2, new Rgba8(255, 128, 64, 255));
		QuadAssert.AssertPixel(shots[2], 2, 2, new Rgba8(204, 153, 102, 51));
	}

	[Theory, GlesLevelsData, Trait(CATEGORY, INTEGRATION)]
	public void SamplerObjectsAreBoundToTheTextureUnit(GlesFeatureLevel level)
	{
		// One 2x1 texture (red, blue) sampled over u in [0, 2] with three sampler objects.
		var log = new ValidationLog();
		var shots = new Dictionary<AddressMode, Screenshot>();
		using (var device = log.CreateDevice(GraphicsBackend.OpenGLES, glesLevel: level))
		{
			using var texture = device.CreateTexture(new TextureDescriptor(2, 1, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst));
			device.Queue.WriteTexture(texture, [255, 0, 0, 255, 0, 0, 255, 255]);
			using var target = device.CreateTexture(new TextureDescriptor(16, 16, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));
			foreach (var mode in new[] { AddressMode.Repeat, AddressMode.ClampToEdge, AddressMode.MirrorRepeat })
			{
				using var sampler = device.CreateSampler(SamplerDescriptor.PointClamp with { AddressModeU = mode, AddressModeV = mode });
				using var quad = new QuadRenderer(device, TextureFormat.Rgba8Unorm, texture.DefaultView, sampler, uvScale: 2);
				quad.Draw(target, Black);
				shots[mode] = Readback.Read(device, target);
			}
		}

		log.AssertClean();
		var red = new Rgba8(255, 0, 0, 255);
		var blue = new Rgba8(0, 0, 255, 255);
		// x = 10: u = 1.3125.
		QuadAssert.AssertPixel(shots[AddressMode.Repeat], 2, 2, red);
		QuadAssert.AssertPixel(shots[AddressMode.Repeat], 10, 2, red);
		QuadAssert.AssertPixel(shots[AddressMode.ClampToEdge], 10, 2, blue);
		QuadAssert.AssertPixel(shots[AddressMode.MirrorRepeat], 10, 2, blue);
	}

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void FramesInFlightWaitOnFencesAndDeferDeletion()
	{
		var log = new ValidationLog();
		using (var device = (GlesDevice)log.CreateDevice(GraphicsBackend.OpenGLES, framesInFlight: 2))
		{
			device.BeginFrame();
			var buffer = (GlesBuffer)device.CreateBuffer(new BufferDescriptor(64, BufferUsage.Uniform | BufferUsage.CopyDst));
			buffer.Dispose();
			// Disposed in frame slot 0: still alive until slot 0 comes round again.
			Assert.True(device.Gl.IsBuffer(buffer.Handle));
			device.EndFrame();
			Assert.Equal(1, device.FrameIndex);
			Assert.True(device.Gl.IsBuffer(buffer.Handle));

			device.BeginFrame();
			device.EndFrame();
			// Back at slot 0: its fence was waited on and its deletions ran.
			Assert.Equal(0, device.FrameIndex);
			Assert.Equal((nint)0, device.CurrentFrame.Fence);
			Assert.False(device.Gl.IsBuffer(buffer.Handle));
		}

		log.AssertClean();
	}

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void TwoDevicesOnOneThreadEachUseTheirOwnContext()
	{
		// Creating the second device makes its context current; every call on the first must switch back.
		var log = new ValidationLog();
		Screenshot first, second;
		using (var a = log.CreateDevice(GraphicsBackend.OpenGLES))
		using (var b = log.CreateDevice(GraphicsBackend.OpenGLES))
		{
			using var quadA = new TexturedQuad(a, TextureFormat.Rgba8Unorm);
			using var targetA = a.CreateTexture(new TextureDescriptor(64, 64, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));
			using var targetB = b.CreateTexture(new TextureDescriptor(8, 8, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));

			var encoderB = b.CreateCommandEncoder();
			encoderB.BeginRenderPass(new RenderPassDescriptor([new RenderPassColorAttachment(targetB.DefaultView, LoadOp.Clear, StoreOp.Store, new Vector4(0, 1, 0, 1))])).End();
			b.Queue.Submit(encoderB.Finish());

			var encoderA = a.CreateCommandEncoder();
			quadA.Draw(encoderA, new RenderPassColorAttachment(targetA.DefaultView, LoadOp.Clear, StoreOp.Store, Black));
			a.Queue.Submit(encoderA.Finish());

			first = Readback.Read(a, targetA);
			second = Readback.Read(b, targetB);
		}

		log.AssertClean();
		QuadAssert.Checkerboard(first, 64, new Rgba8(0, 0, 0, 255));
		QuadAssert.AssertPixel(second, 4, 4, new Rgba8(0, 255, 0, 255));
	}

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void ReadbackConvertsNonRgbaFormatsOnTheCpu()
	{
		var log = new ValidationLog();
		using (var device = log.CreateDevice(GraphicsBackend.OpenGLES))
		{
			foreach (var (format, texels) in new (TextureFormat, byte[])[]
			{
				(TextureFormat.R8Unorm, [10, 20, 30, 40]),
				(TextureFormat.Rg8Unorm, [1, 2, 3, 4, 5, 6, 7, 8]),
				(TextureFormat.Bgra8Unorm, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]),
			})
			{
				using var texture = device.CreateTexture(new TextureDescriptor(2, 2, format, TextureUsage.CopyDst | TextureUsage.CopySrc | TextureUsage.RenderAttachment));
				device.Queue.WriteTexture(texture, texels);
				var bpp = (uint)format.BytesPerPixel();
				using var readback = device.CreateBuffer(new BufferDescriptor((ulong)texels.Length, BufferUsage.MapRead | BufferUsage.CopyDst));
				var encoder = device.CreateCommandEncoder();
				encoder.CopyTextureToBuffer(texture, TextureRegion.Whole(texture), readback, 0, 2 * bpp);
				device.Queue.Submit(encoder.Finish());
				var bytes = new byte[texels.Length];
				readback.Read(0, bytes);
				Assert.True(texels.AsSpan().SequenceEqual(bytes), $"{format}: read [{string.Join(", ", bytes)}], wrote [{string.Join(", ", texels)}].");
			}
		}

		log.AssertClean();
	}
}
