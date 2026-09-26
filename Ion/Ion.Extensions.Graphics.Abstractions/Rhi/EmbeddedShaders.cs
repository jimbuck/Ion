using System.Reflection;

namespace Ion.Extensions.Graphics.Rhi;

/// <summary>
/// Loads shaders compiled at build time by <c>Ion.Shaders.targets</c>, which embeds <c>Shaders/&lt;file&gt;.spv</c>
/// (SPIR-V) and <c>Shaders/&lt;file&gt;.es.glsl</c> (GLSL ES 3.10) for every <c>IonShader</c> item.
/// </summary>
public static class EmbeddedShaders
{
	/// <summary>
	/// Reads the compiled form of <paramref name="fileName"/> (for example <c>"sprite.vert"</c>) in
	/// <paramref name="language"/> from <paramref name="assembly"/>'s resources.
	/// </summary>
	/// <exception cref="FileNotFoundException">The assembly has no such shader.</exception>
	public static byte[] Load(Assembly assembly, string fileName, ShaderLanguage language)
	{
		ArgumentNullException.ThrowIfNull(assembly);
		ArgumentException.ThrowIfNullOrEmpty(fileName);
		var resource = ResourceName(fileName, language);
		using var stream = assembly.GetManifestResourceStream(resource)
			?? throw new FileNotFoundException($"Shader resource '{resource}' not found in {assembly.GetName().Name}. Add the shader as an IonShader item and import Ion.Shaders.targets.", resource);
		var bytes = new byte[stream.Length];
		stream.ReadExactly(bytes);
		return bytes;
	}

	/// <summary>
	/// A shader module descriptor for <paramref name="fileName"/> in <paramref name="device"/>'s shader language, with the
	/// stage taken from the extension (<c>.vert</c> or <c>.frag</c>).
	/// </summary>
	public static ShaderModuleDescriptor Descriptor(IGraphicsDevice device, Assembly assembly, string fileName)
	{
		ArgumentNullException.ThrowIfNull(device);
		var stage = Path.GetExtension(fileName) switch
		{
			".vert" => ShaderStage.Vertex,
			".frag" => ShaderStage.Fragment,
			_ => throw new ArgumentException($"Cannot tell the stage of '{fileName}' (use .vert or .frag).", nameof(fileName)),
		};
		return new ShaderModuleDescriptor(Load(assembly, fileName, device.ShaderLanguage), stage, device.ShaderLanguage, fileName);
	}

	/// <summary>The manifest resource name of a compiled shader.</summary>
	public static string ResourceName(string fileName, ShaderLanguage language) =>
		language == ShaderLanguage.SpirV ? $"Shaders/{fileName}.spv" : $"Shaders/{fileName}.es.glsl";
}
