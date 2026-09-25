using System.Text;

using Silk.NET.Core.Native;
using Silk.NET.Shaderc;

using Cross = Silk.NET.SPIRV.Cross.Cross;
using CrossBackend = Silk.NET.SPIRV.Cross.Backend;
using CrossCaptureMode = Silk.NET.SPIRV.Cross.CaptureMode;
using CrossCombinedImageSampler = Silk.NET.SPIRV.Cross.CombinedImageSampler;
using CrossCompiler = Silk.NET.SPIRV.Cross.Compiler;
using CrossCompilerOption = Silk.NET.SPIRV.Cross.CompilerOption;
using CrossCompilerOptions = Silk.NET.SPIRV.Cross.CompilerOptions;
using CrossContext = Silk.NET.SPIRV.Cross.Context;
using CrossParsedIr = Silk.NET.SPIRV.Cross.ParsedIr;
using CrossResult = Silk.NET.SPIRV.Cross.Result;
using ShadercCompiler = Silk.NET.Shaderc.Compiler;

namespace Ion.Shaders;

/// <summary>
/// Compiles GLSL 4.5 (Vulkan dialect) to SPIR-V with Shaderc and translates SPIR-V to GLSL ES 3.10 with SPIRV-Cross.
/// </summary>
public static unsafe class ShaderCompiler
{
	/// <summary>The shader stage of a file, from its extension (<c>.vert</c>, <c>.frag</c>, <c>.comp</c>).</summary>
	public static ShaderKind KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
	{
		".vert" => ShaderKind.VertexShader,
		".frag" => ShaderKind.FragmentShader,
		".comp" => ShaderKind.ComputeShader,
		_ => throw new ArgumentException($"Unknown shader stage for '{path}' (use .vert, .frag or .comp)."),
	};

	/// <summary>
	/// Compiles GLSL source to SPIR-V (Vulkan 1.0 environment, so it runs on every Vulkan device).
	/// </summary>
	/// <exception cref="ShaderCompilationException">The source does not compile; the message has Shaderc's diagnostics.</exception>
	public static byte[] CompileToSpirV(string source, ShaderKind kind, string fileName, bool optimize = true)
	{
		var shaderc = Shaderc.GetApi();
		var compiler = shaderc.CompilerInitialize();
		var options = shaderc.CompileOptionsInitialize();
		try
		{
			shaderc.CompileOptionsSetTargetEnv(options, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan10);
			shaderc.CompileOptionsSetSourceLanguage(options, SourceLanguage.Glsl);
			shaderc.CompileOptionsSetOptimizationLevel(options, optimize ? OptimizationLevel.Performance : OptimizationLevel.Zero);
			shaderc.CompileOptionsSetGenerateDebugInfo(options);

			var byteCount = (nuint)Encoding.UTF8.GetByteCount(source);
			var result = shaderc.CompileIntoSpv(compiler, source, byteCount, kind, fileName, "main", options);
			try
			{
				if (shaderc.ResultGetCompilationStatus(result) != CompilationStatus.Success)
				{
					throw new ShaderCompilationException(shaderc.ResultGetErrorMessageS(result));
				}

				return new ReadOnlySpan<byte>(shaderc.ResultGetBytes(result), (int)shaderc.ResultGetLength(result)).ToArray();
			}
			finally
			{
				shaderc.ResultRelease(result);
			}
		}
		finally
		{
			shaderc.CompileOptionsRelease(options);
			shaderc.CompilerRelease(compiler);
		}
	}

	/// <summary>
	/// Translates SPIR-V to GLSL ES 3.10 source for the GLES backend: separate textures and samplers are combined (GLES has
	/// only combined samplers; each combined sampler is named <c>texture_sampler</c>), and depth is remapped from
	/// WebGPU/Vulkan's [0, 1] clip range to GL's [-1, 1].
	/// </summary>
	/// <exception cref="ShaderCompilationException">SPIRV-Cross cannot translate the module.</exception>
	public static string TranslateToGlslEs(ReadOnlySpan<byte> spirv, uint version = 310)
	{
		var cross = Cross.GetApi();
		CrossContext* context = null;
		Check(cross, null, cross.ContextCreate(&context));
		try
		{
			CrossParsedIr* ir;
			fixed (byte* p = spirv) Check(cross, context, cross.ContextParseSpirv(context, (uint*)p, (nuint)(spirv.Length / 4), &ir));

			CrossCompiler* compiler;
			Check(cross, context, cross.ContextCreateCompiler(context, CrossBackend.Glsl, ir, CrossCaptureMode.TakeOwnership, &compiler));

			CrossCompilerOptions* options;
			Check(cross, context, cross.CompilerCreateCompilerOptions(compiler, &options));
			Check(cross, context, cross.CompilerOptionsSetUint(options, CrossCompilerOption.GlslVersion, version));
			Check(cross, context, cross.CompilerOptionsSetBool(options, CrossCompilerOption.GlslES, 1));
			Check(cross, context, cross.CompilerOptionsSetBool(options, CrossCompilerOption.FixupDepthConvention, 1));
			Check(cross, context, cross.CompilerInstallCompilerOptions(compiler, options));

			// GLES has no separate samplers: combine every (texture, sampler) pair used together into a sampler2D.
			Check(cross, context, cross.CompilerBuildCombinedImageSamplers(compiler));
			CrossCombinedImageSampler* combined;
			nuint count;
			Check(cross, context, cross.CompilerGetCombinedImageSamplers(compiler, &combined, &count));
			for (nuint i = 0; i < count; i++)
			{
				var imageName = SilkMarshal.PtrToString((nint)cross.CompilerGetName(compiler, combined[i].ImageId)) ?? "texture";
				var samplerName = SilkMarshal.PtrToString((nint)cross.CompilerGetName(compiler, combined[i].SamplerId)) ?? "sampler";
				cross.CompilerSetName(compiler, combined[i].CombinedId, $"{imageName}_{samplerName}");
			}

			byte* source;
			Check(cross, context, cross.CompilerCompile(compiler, &source));
			return SilkMarshal.PtrToString((nint)source) ?? string.Empty;
		}
		finally
		{
			cross.ContextDestroy(context);
		}
	}

	private static void Check(Cross cross, CrossContext* context, CrossResult result)
	{
		if (result == CrossResult.Success) return;
		var message = context is null ? null : SilkMarshal.PtrToString((nint)cross.ContextGetLastErrorString(context));
		throw new ShaderCompilationException($"SPIRV-Cross failed ({result}): {message}");
	}
}

/// <summary>A shader failed to compile or translate.</summary>
public sealed class ShaderCompilationException(string message) : Exception(message);
