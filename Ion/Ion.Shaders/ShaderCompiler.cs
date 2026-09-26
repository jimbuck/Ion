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
using CrossReflectedResource = Silk.NET.SPIRV.Cross.ReflectedResource;
using CrossResources = Silk.NET.SPIRV.Cross.Resources;
using CrossResourceType = Silk.NET.SPIRV.Cross.ResourceType;
using CrossResult = Silk.NET.SPIRV.Cross.Result;
using Decoration = Silk.NET.SPIRV.Decoration;
using ExecutionModel = Silk.NET.SPIRV.ExecutionModel;
using GlesBindings = Ion.Extensions.Graphics.Rhi.GlesBindings;
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
	/// Replaces every <c>#include "file"</c> line of <paramref name="source"/> with the contents of that file, resolved
	/// relative to <paramref name="directory"/> (includes nest; each file is included once per shader). A <c>#line</c>
	/// directive after each included file keeps the including file's line numbers in diagnostics. The included files are
	/// added to <paramref name="included"/> (for incremental builds).
	/// </summary>
	/// <exception cref="ShaderCompilationException">An included file does not exist.</exception>
	public static string ResolveIncludes(string source, string directory, ICollection<string>? included = null)
	{
		ArgumentNullException.ThrowIfNull(source);
		var seen = new HashSet<string>(StringComparer.Ordinal);
		return Resolve(source, directory);

		string Resolve(string text, string dir)
		{
			if (!text.Contains("#include", StringComparison.Ordinal)) return text;
			var lines = text.Split('\n');
			var result = new StringBuilder(text.Length);
			for (var i = 0; i < lines.Length; i++)
			{
				var line = lines[i];
				var trimmed = line.TrimStart();
				if (!trimmed.StartsWith("#include", StringComparison.Ordinal))
				{
					result.Append(line);
					if (i < lines.Length - 1) result.Append('\n');
					continue;
				}

				var open = trimmed.IndexOf('"');
				var close = open < 0 ? -1 : trimmed.IndexOf('"', open + 1);
				if (close < 0) throw new ShaderCompilationException($"{i + 1}: error: malformed #include (use #include \"file\").");
				var path = Path.GetFullPath(Path.Combine(dir, trimmed[(open + 1)..close]));
				if (!File.Exists(path)) throw new ShaderCompilationException($"{i + 1}: error: included file '{path}' not found.");
				if (seen.Add(path))
				{
					included?.Add(path);
					result.Append(Resolve(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), Path.GetDirectoryName(path)!)).Append('\n');
				}

				// The next line of the including file keeps its own number.
				result.Append("#line ").Append(i + 2).Append('\n');
			}

			return result.ToString();
		}
	}

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
	/// only combined samplers; each combined sampler is named <c>texture_sampler</c>), depth is remapped from
	/// WebGPU/Vulkan's [0, 1] clip range to GL's [-1, 1], clip-space y is negated (the backend renders upside down in GL
	/// terms so that texture row 0 is the top row, as in WebGPU), (set, binding) pairs are flattened to binding points and
	/// texture units with <see cref="GlesBindings"/> and listed in a flattening table appended as comments, and the
	/// varyings between stages are named by location (<c>ion_var_N</c>) so that stages link by name on GLSL ES 3.00 too.
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
			Check(cross, context, cross.CompilerOptionsSetBool(options, CrossCompilerOption.FlipVertexY, 1));
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

			var table = FlattenBindings(cross, context, compiler, combined, count);

			byte* source;
			Check(cross, context, cross.CompilerCompile(compiler, &source));
			var glsl = SilkMarshal.PtrToString((nint)source) ?? string.Empty;
			var result = new StringBuilder(glsl);
			if (table.Count > 0)
			{
				if (!glsl.EndsWith('\n')) result.Append('\n');
				result.Append("\n// Ion GLES binding table: (set, binding) flattened by GlesBindings.Slot.\n");
				foreach (var line in table) result.Append(line).Append('\n');
			}

			return result.ToString();
		}
		finally
		{
			cross.ContextDestroy(context);
		}
	}

	/// <summary>
	/// Rewrites the binding decorations to the flattened GLES slots, names the varyings by location, and returns the lines
	/// of the flattening table.
	/// </summary>
	private static List<string> FlattenBindings(Cross cross, CrossContext* context, CrossCompiler* compiler, CrossCombinedImageSampler* combined, nuint combinedCount)
	{
		var table = new List<string>();
		CrossResources* resources;
		Check(cross, context, cross.CompilerCreateShaderResources(compiler, &resources));

		foreach (var (type, kind) in (ReadOnlySpan<(CrossResourceType, string)>)[(CrossResourceType.UniformBuffer, "ubo"), (CrossResourceType.StorageBuffer, "ssbo")])
		{
			CrossReflectedResource* list;
			nuint count;
			Check(cross, context, cross.ResourcesGetResourceListForType(resources, type, &list, &count));
			for (nuint i = 0; i < count; i++)
			{
				var set = cross.CompilerGetDecoration(compiler, list[i].Id, Decoration.DescriptorSet);
				var binding = cross.CompilerGetDecoration(compiler, list[i].Id, Decoration.Binding);
				var name = SilkMarshal.PtrToString((nint)cross.CompilerGetName(compiler, list[i].BaseTypeId));
				if (string.IsNullOrEmpty(name)) name = SilkMarshal.PtrToString((nint)list[i].Name) ?? "block";
				cross.CompilerSetDecoration(compiler, list[i].Id, Decoration.Binding, (uint)_slot(set, binding));
				cross.CompilerUnsetDecoration(compiler, list[i].Id, Decoration.DescriptorSet);
				table.Add(GlesBindings.FormatBlock(kind, name, set, binding));
			}
		}

		var units = new Dictionary<int, string>();
		for (nuint i = 0; i < combinedCount; i++)
		{
			var image = combined[i].ImageId;
			var sampler = combined[i].SamplerId;
			var set = cross.CompilerGetDecoration(compiler, image, Decoration.DescriptorSet);
			var binding = cross.CompilerGetDecoration(compiler, image, Decoration.Binding);
			var samplerSet = cross.CompilerGetDecoration(compiler, sampler, Decoration.DescriptorSet);
			var samplerBinding = cross.CompilerGetDecoration(compiler, sampler, Decoration.Binding);
			var name = SilkMarshal.PtrToString((nint)cross.CompilerGetName(compiler, combined[i].CombinedId)) ?? "sampler";
			var slot = _slot(set, binding);
			if (units.TryGetValue(slot, out var other))
			{
				throw new ShaderCompilationException($"GLES: '{name}' and '{other}' sample the texture at set {set}, binding {binding} with different samplers; the GLES backend binds one sampler per texture unit, so sample each texture with one sampler.");
			}

			units.Add(slot, name);
			cross.CompilerSetDecoration(compiler, combined[i].CombinedId, Decoration.Binding, (uint)slot);
			table.Add(GlesBindings.FormatSampler(name, set, binding, samplerSet, samplerBinding));
		}

		// Varyings named by location, so the stages also link by name where locations are not allowed (GLSL ES 3.00).
		var model = cross.CompilerGetExecutionModel(compiler);
		var varyings = model switch
		{
			ExecutionModel.Vertex => CrossResourceType.StageOutput,
			ExecutionModel.Fragment => CrossResourceType.StageInput,
			_ => (CrossResourceType?)null,
		};
		if (varyings is { } varyingType)
		{
			CrossReflectedResource* list;
			nuint count;
			Check(cross, context, cross.ResourcesGetResourceListForType(resources, varyingType, &list, &count));
			for (nuint i = 0; i < count; i++)
			{
				var location = cross.CompilerGetDecoration(compiler, list[i].Id, Decoration.Location);
				cross.CompilerSetName(compiler, list[i].Id, $"ion_var_{location}");
			}
		}

		return table;
	}

	private static int _slot(uint set, uint binding)
	{
		try
		{
			return GlesBindings.Slot(set, binding);
		}
		catch (ArgumentOutOfRangeException ex)
		{
			throw new ShaderCompilationException($"GLES: set {set}, binding {binding} cannot be flattened: {ex.Message}");
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
