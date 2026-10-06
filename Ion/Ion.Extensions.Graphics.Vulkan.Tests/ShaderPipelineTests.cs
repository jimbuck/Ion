using System.Text;

using Silk.NET.Shaderc;

using Ion.Examples.Quad;
using Ion.Extensions.Graphics.Rhi;
using Ion.Shaders;

namespace Ion.Extensions.Graphics.Vulkan.Tests;

/// <summary>
/// The build-time shader pipeline: GLSL 4.5 to SPIR-V (Shaderc), SPIR-V to GLSL ES 3.10 (SPIRV-Cross), embedded resources.
/// </summary>
public class ShaderPipelineTests
{
	private static readonly System.Reflection.Assembly QuadAssembly = typeof(TexturedQuad).Assembly;

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData("textured_quad.vert")]
	[InlineData("textured_quad.frag")]
	public void TheBuildEmbedsSpirVForEveryShader(string name)
	{
		var spirv = EmbeddedShaders.Load(QuadAssembly, name, ShaderLanguage.SpirV);
		Assert.True(spirv.Length > 20 && spirv.Length % 4 == 0);
		Assert.Equal(0x07230203u, BitConverter.ToUInt32(spirv, 0));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheBuildEmbedsGlslEsTranslatedBySpirvCross()
	{
		var fragment = Encoding.UTF8.GetString(EmbeddedShaders.Load(QuadAssembly, "textured_quad.frag", ShaderLanguage.GlslEs));
		Assert.StartsWith("#version 310 es", fragment);
		// The separate texture and sampler are combined into one sampler2D for GLES.
		Assert.Contains("sampler2D uTexture_uSampler", fragment);

		var vertex = Encoding.UTF8.GetString(EmbeddedShaders.Load(QuadAssembly, "textured_quad.vert", ShaderLanguage.GlslEs));
		Assert.StartsWith("#version 310 es", vertex);
		Assert.Contains("uniform Transform", vertex);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SpirvCrossTranslatesAtRunTimeToo()
	{
		var spirv = EmbeddedShaders.Load(QuadAssembly, "textured_quad.vert", ShaderLanguage.SpirV);
		var gles = ShaderCompiler.TranslateToGlslEs(spirv);
		Assert.StartsWith("#version 310 es", gles);
		Assert.Contains("gl_Position", gles);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ShadercCompilesGlsl()
	{
		const string source = "#version 450\nlayout(location = 0) out vec4 color;\nvoid main() { color = vec4(1.0); }\n";
		var spirv = ShaderCompiler.CompileToSpirV(source, ShaderKind.FragmentShader, "white.frag");
		Assert.Equal(0x07230203u, BitConverter.ToUInt32(spirv, 0));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void CompileErrorsCarryTheFileAndLine()
	{
		const string source = "#version 450\nvoid main() {\n  undefined_call();\n}\n";
		var ex = Assert.Throws<ShaderCompilationException>(() => ShaderCompiler.CompileToSpirV(source, ShaderKind.VertexShader, "broken.vert"));
		Assert.Contains("broken.vert:3", ex.Message);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void IncludesAreResolvedRelativeToTheShaderAndKeepLineNumbers()
	{
		var folder = Path.Combine(Path.GetTempPath(), $"ion-include-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Combine(folder, "lib"));
		try
		{
			File.WriteAllText(Path.Combine(folder, "lib", "color.glsl"), "#include \"white.glsl\"\nvec4 twice() { return white() * 2.0; }\n");
			File.WriteAllText(Path.Combine(folder, "lib", "white.glsl"), "vec4 white() { return vec4(1.0); }\n");
			const string source = "#version 450\n#include \"lib/color.glsl\"\n#include \"lib/white.glsl\"\nlayout(location = 0) out vec4 color;\nvoid main() { color = twice(); undefined_call(); }\n";
			var included = new List<string>();
			var resolved = ShaderCompiler.ResolveIncludes(source, folder, included);
			Assert.Equal(2, included.Count);
			Assert.Single(System.Text.RegularExpressions.Regex.Matches(resolved, "vec4 white\\(\\)"));

			// The error on line 5 of the shader is still reported at line 5.
			var ex = Assert.Throws<ShaderCompilationException>(() => ShaderCompiler.CompileToSpirV(resolved, ShaderKind.FragmentShader, "main.frag"));
			Assert.Contains("main.frag:5", ex.Message);

			Assert.Throws<ShaderCompilationException>(() => ShaderCompiler.ResolveIncludes("#include \"missing.glsl\"\n", folder));
		}
		finally
		{
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ComputeShadersTranslateWithStorageBlocksAndImagesInTheBindingTable()
	{
		const string source = """
			#version 450
			layout(local_size_x = 8, local_size_y = 8) in;
			layout(set = 0, binding = 0) uniform Params { float gain; } uParams;
			layout(set = 0, binding = 1) readonly buffer Input { float values[]; } uInput;
			layout(set = 1, binding = 2, rgba8) uniform writeonly image2D uImage;
			void main()
			{
				ivec2 p = ivec2(gl_GlobalInvocationID.xy);
				imageStore(uImage, p, vec4(uInput.values[p.x] * uParams.gain));
			}
			""";
		var spirv = ShaderCompiler.CompileToSpirV(source, ShaderCompiler.KindOf("kernel.comp"), "kernel.comp");
		var gles = ShaderCompiler.TranslateToGlslEs(spirv);
		Assert.StartsWith("#version 310 es", gles);
		Assert.Contains("local_size_x = 8", gles);

		// The flattening table (parsed by the GLES backend with GlesBindings.ParseTable).
		Assert.Contains("// ion:ubo Params set=0 binding=0 slot=0", gles);
		Assert.Contains("// ion:ssbo Input set=0 binding=1 slot=1", gles);
		Assert.Contains("// ion:image uImage set=1 binding=2 slot=10", gles);
		// The image's binding qualifier is the flattened image unit.
		Assert.Matches(@"layout\(binding = 10, rgba8\) uniform (highp |mediump )?writeonly (highp |mediump )?image2D uImage", gles);
	}

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void ComputeShadersEmbedWithTheirStage()
	{
		using var device = VulkanDevice.Create(new VulkanDeviceOptions());
		var assembly = typeof(Rhi.Tests.ComputeContractTests).Assembly;
		var descriptor = EmbeddedShaders.Descriptor(device, assembly, "scale.comp");
		Assert.Equal(ShaderStage.Compute, descriptor.Stage);
		Assert.Equal(0x07230203u, BitConverter.ToUInt32(descriptor.Code.Span));
		Assert.Throws<ArgumentException>(() => EmbeddedShaders.Descriptor(device, assembly, "scale.compute"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void MissingShadersExplainHowToAddThem()
	{
		var ex = Assert.Throws<FileNotFoundException>(() => EmbeddedShaders.Load(QuadAssembly, "nope.vert", ShaderLanguage.SpirV));
		Assert.Contains("IonShader", ex.Message);
	}

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void TheVulkanBackendRejectsNonSpirVCode()
	{
		using var device = VulkanDevice.Create(new VulkanDeviceOptions());
		var gles = EmbeddedShaders.Load(QuadAssembly, "textured_quad.vert", ShaderLanguage.GlslEs);
		Assert.Throws<NotSupportedException>(() => device.CreateShaderModule(new ShaderModuleDescriptor(gles, ShaderStage.Vertex, ShaderLanguage.GlslEs)));
		Assert.Throws<ArgumentException>(() => device.CreateShaderModule(new ShaderModuleDescriptor(new byte[8], ShaderStage.Vertex)));
	}
}
