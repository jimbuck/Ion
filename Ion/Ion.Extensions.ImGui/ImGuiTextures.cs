using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.ImGui;

/// <summary>
/// The textures ImGui draws, by <c>ImTextureID</c>: the ones ImGui creates itself (the font atlas, owned and released
/// here) and the game's (registered with <see cref="ImGuiHost.Register"/>). Bind groups are created on first draw.
/// </summary>
internal sealed class ImGuiTextures
{
	private readonly Dictionary<nint, Entry> _entries = [];
	private nint _next;

	private sealed class Entry(ITexture texture, bool owned, string? label)
	{
		public ITexture Texture { get; } = texture;
		public bool Owned { get; } = owned;
		public string? Label { get; } = label;
		public IBindGroup? Group { get; set; }
	}

	/// <summary>Creates bind groups for <see cref="BindGroup"/> (set when the renderer exists).</summary>
	public Func<ITextureView, string?, IBindGroup>? BindGroupFactory { get; set; }

	public int Count => _entries.Count;

	public nint Add(ITexture texture, bool owned, string? label)
	{
		var id = ++_next;
		_entries[id] = new Entry(texture, owned, label);
		return id;
	}

	public ITexture? Find(nint id) => _entries.TryGetValue(id, out var entry) ? entry.Texture : null;

	public IBindGroup? BindGroup(nint id)
	{
		if (!_entries.TryGetValue(id, out var entry) || BindGroupFactory is not { } factory) return null;
		return entry.Group ??= factory(entry.Texture.DefaultView, entry.Label);
	}

	/// <summary>Forgets <paramref name="id"/>, releasing its bind group, and the texture when it is ImGui's own.</summary>
	public bool Release(nint id)
	{
		if (!_entries.Remove(id, out var entry)) return false;
		entry.Group?.Dispose();
		if (entry.Owned) entry.Texture.Dispose();
		return true;
	}

	/// <summary>Releases everything (the renderer is going away).</summary>
	public void Clear()
	{
		foreach (var entry in _entries.Values)
		{
			entry.Group?.Dispose();
			if (entry.Owned) entry.Texture.Dispose();
		}

		_entries.Clear();
	}
}
