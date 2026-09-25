using System.Globalization;

namespace Ion.Extensions.Graphics.Rhi;

/*
 * This file is compiled into two assemblies: Ion.Extensions.Graphics.Abstractions (public, used by the GLES backend) and
 * the build-time shader tool Ion.Shaders (linked, internal), so the shader translation and the backend flatten bind group
 * bindings with the same code.
 */

/// <summary>
/// The binding-point scheme of the OpenGL ES backend: how WebGPU-style (group, binding) pairs become GLES uniform block
/// binding points and texture units. Shared by the shader tool (which writes the numbers into the translated GLSL ES as
/// <c>layout(binding = N)</c> and into a flattening table) and the GLES backend (which binds resources to them).
/// </summary>
/// <remarks>
/// <para>
/// The scheme is fixed, not packed per pipeline, so each shader stage can be translated on its own: every group owns
/// <see cref="BindingsPerGroup"/> consecutive slots, and the slot of a resource is <c>group * 8 + binding</c> in the
/// namespace of its kind (uniform blocks, texture units, storage blocks). A texture and the sampler it is used with are one
/// combined sampler in GLSL ES; its texture unit is the texture's slot, and the sampler object bound to that unit is the
/// sampler the shader combined it with (recorded in the flattening table).
/// </para>
/// <para>
/// Limits: binding numbers below 8 and at most 4 groups. OpenGL ES 3.1 guarantees 36 uniform buffer bindings and 48
/// combined texture units, which covers all 4 groups; ES 3.0 guarantees 24 uniform buffer bindings (groups 0 to 2) and 32
/// texture units. The backend checks the device's limits when it creates a pipeline.
/// </para>
/// <para>
/// The flattening table is a block of comment lines appended to every translated GLSL ES source, one per resource, for
/// example <c>// ion:ubo Transform set=0 binding=0 slot=0</c> and
/// <c>// ion:sampler uTexture_uSampler set=0 binding=1 sampler-set=0 sampler-binding=2 slot=1</c>. The ES 3.0 fallback,
/// whose GLSL ES 3.00 has no binding qualifiers, assigns the slots by name from it.
/// </para>
/// </remarks>
#if ION_SHADERS
internal
#else
public
#endif
static class GlesBindings
{
	/// <summary>The slots each bind group owns in every namespace.</summary>
	public const int BindingsPerGroup = 8;

	/// <summary>The number of bind groups the scheme covers.</summary>
	public const int MaxGroups = 4;

	/// <summary>The prefix of the flattening table's comment lines.</summary>
	public const string TablePrefix = "// ion:";

	/// <summary>The flattened slot of (<paramref name="group"/>, <paramref name="binding"/>).</summary>
	/// <exception cref="ArgumentOutOfRangeException">The group or binding is outside the scheme.</exception>
	public static int Slot(uint group, uint binding)
	{
		if (group >= MaxGroups) throw new ArgumentOutOfRangeException(nameof(group), group, $"The GLES backend supports bind groups 0 to {MaxGroups - 1}.");
		if (binding >= BindingsPerGroup) throw new ArgumentOutOfRangeException(nameof(binding), binding, $"The GLES backend supports binding numbers 0 to {BindingsPerGroup - 1} in every group.");
		return (int)(group * BindingsPerGroup + binding);
	}

	/// <summary>Formats a uniform block (or storage block, <paramref name="kind"/> <c>ssbo</c>) line of the flattening table.</summary>
	public static string FormatBlock(string kind, string name, uint group, uint binding) =>
		string.Create(CultureInfo.InvariantCulture, $"{TablePrefix}{kind} {name} set={group} binding={binding} slot={Slot(group, binding)}");

	/// <summary>Formats a combined image sampler line of the flattening table.</summary>
	public static string FormatSampler(string name, uint textureGroup, uint textureBinding, uint samplerGroup, uint samplerBinding) =>
		string.Create(CultureInfo.InvariantCulture, $"{TablePrefix}sampler {name} set={textureGroup} binding={textureBinding} sampler-set={samplerGroup} sampler-binding={samplerBinding} slot={Slot(textureGroup, textureBinding)}");

	/// <summary>
	/// Parses the flattening table out of a translated GLSL ES source. Lines that are not table lines are ignored, so a
	/// source without a table yields an empty list.
	/// </summary>
	public static List<GlesBindingEntry> ParseTable(string source)
	{
		ArgumentNullException.ThrowIfNull(source);
		var entries = new List<GlesBindingEntry>();
		foreach (var raw in source.Split('\n'))
		{
			var line = raw.Trim();
			if (!line.StartsWith(TablePrefix, StringComparison.Ordinal)) continue;
			var parts = line[TablePrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 2) continue;
			var kind = parts[0] switch
			{
				"ubo" => GlesBindingKind.UniformBlock,
				"ssbo" => GlesBindingKind.StorageBlock,
				"sampler" => GlesBindingKind.Sampler,
				_ => (GlesBindingKind?)null,
			};
			if (kind is null) continue;

			uint group = 0, binding = 0, samplerGroup = 0, samplerBinding = 0;
			var slot = -1;
			for (var i = 2; i < parts.Length; i++)
			{
				var eq = parts[i].IndexOf('=');
				if (eq < 0) continue;
				var key = parts[i][..eq];
				var value = parts[i][(eq + 1)..];
				switch (key)
				{
					case "set": group = uint.Parse(value, CultureInfo.InvariantCulture); break;
					case "binding": binding = uint.Parse(value, CultureInfo.InvariantCulture); break;
					case "sampler-set": samplerGroup = uint.Parse(value, CultureInfo.InvariantCulture); break;
					case "sampler-binding": samplerBinding = uint.Parse(value, CultureInfo.InvariantCulture); break;
					case "slot": slot = int.Parse(value, CultureInfo.InvariantCulture); break;
				}
			}

			entries.Add(new GlesBindingEntry(kind.Value, parts[1], group, binding, slot < 0 ? Slot(group, binding) : slot, samplerGroup, samplerBinding));
		}

		return entries;
	}
}

/// <summary>The kind of a <see cref="GlesBindingEntry"/>.</summary>
#if ION_SHADERS
internal
#else
public
#endif
enum GlesBindingKind
{
	/// <summary>A uniform block (a uniform buffer binding).</summary>
	UniformBlock,
	/// <summary>A shader storage block (a storage buffer binding).</summary>
	StorageBlock,
	/// <summary>A combined image sampler (a texture binding and the sampler binding it is sampled with).</summary>
	Sampler,
}

/// <summary>One line of the flattening table (see <see cref="GlesBindings"/>).</summary>
/// <param name="Kind">The kind of resource.</param>
/// <param name="Name">The GLSL ES name: the block name, or the combined sampler's uniform name.</param>
/// <param name="Group">The bind group of the buffer or texture.</param>
/// <param name="Binding">The binding of the buffer or texture in its group.</param>
/// <param name="Slot">The binding point or texture unit.</param>
/// <param name="SamplerGroup">For <see cref="GlesBindingKind.Sampler"/>, the group of the sampler binding.</param>
/// <param name="SamplerBinding">For <see cref="GlesBindingKind.Sampler"/>, the binding of the sampler.</param>
#if ION_SHADERS
internal
#else
public
#endif
readonly record struct GlesBindingEntry(GlesBindingKind Kind, string Name, uint Group, uint Binding, int Slot, uint SamplerGroup = 0, uint SamplerBinding = 0);
