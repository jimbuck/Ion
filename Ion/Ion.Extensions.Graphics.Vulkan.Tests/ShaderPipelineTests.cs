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
