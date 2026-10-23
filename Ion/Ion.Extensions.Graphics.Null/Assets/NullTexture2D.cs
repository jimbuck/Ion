using SixLabors.ImageSharp;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Graphics;

/// <summary>
/// A texture that only knows its size: no pixels, no GPU resource. Created by <see cref="NullTexture2DLoader"/>.
/// </summary>
public sealed class NullTexture2D : ITexture2D
{
	private static long _nextId;

	public NullTexture2D(string name, uint width, uint height)
	{
		Name = name;
		Width = width;
		Height = height;
		MipLevels = ComputeMipLevels(width, height);
	}

	public nint Id { get; } = (nint)Interlocked.Increment(ref _nextId);

	public string Name { get; }

	public uint Width { get; private set; }

	public uint Height { get; private set; }

	/// <summary>
	/// The mip level count the 2D renderer's texture loader generates for this size (a full chain down to 1x1).
	/// </summary>
	public uint MipLevels { get; private set; }

	/// <summary>
	/// How many times the texture has been reloaded in place (asset hot reload).
	/// </summary>
	public int ReloadCount { get; private set; }

	/// <summary>
	/// Takes the size of <paramref name="source"/>, as a hot reload does.
	/// </summary>
	internal void ReloadFrom(NullTexture2D source)
	{
		Width = source.Width;
		Height = source.Height;
		MipLevels = source.MipLevels;
		ReloadCount++;
	}

	/// <summary>
	/// True once the asset has been disposed.
	/// </summary>
	public bool IsDisposed { get; private set; }

	public void Dispose() => IsDisposed = true;

	internal static uint ComputeMipLevels(uint width, uint height)
	{
		var largest = Math.Max(width, height);
		return largest == 0 ? 1 : 1 + (uint)Math.Floor(Math.Log2(largest));
	}
}

/// <summary>
/// Loads <see cref="ITexture2D"/> assets for the headless backend: reads the image header (with ImageSharp's
/// <see cref="Image.Identify(Stream)"/>) for its width and height without decoding pixels or touching a GPU. Supports hot
/// reload in place: a reloaded <see cref="NullTexture2D"/> takes the new file's size.
/// </summary>
public sealed class NullTexture2DLoader(IPersistentStorage storage) : IAssetLoader<ITexture2D>, IReloadableAssetLoader
{
	public Type AssetType { get; } = typeof(ITexture2D);

	/// <exception cref="FileNotFoundException">The file does not exist.</exception>
	/// <exception cref="InvalidDataException">The file is not an image format ImageSharp recognizes.</exception>
	public ITexture2D Load(string path)
	{
		using var stream = storage.Assets.Read(path);
		return Read(path, stream);
	}

	/// <summary>
	/// Reads the file again and updates <paramref name="asset"/> (a <see cref="NullTexture2D"/>) in place. Returns false for
	/// any other asset type.
	/// </summary>
	/// <exception cref="FileNotFoundException">The file does not exist.</exception>
	/// <exception cref="InvalidDataException">The file is not an image format ImageSharp recognizes.</exception>
	public bool TryReload(IAsset asset, string path)
	{
		if (asset is not NullTexture2D texture) return false;

		using var stream = storage.Assets.Read(path);
		texture.ReloadFrom(Read(texture.Name, stream));
		return true;
	}

	/// <summary>
	/// Reads the size of the image in <paramref name="stream"/> into a <see cref="NullTexture2D"/> named <paramref name="name"/>.
	/// </summary>
	/// <exception cref="InvalidDataException">The stream is not an image format ImageSharp recognizes.</exception>
	public static NullTexture2D Read(string name, Stream stream)
	{
		ImageInfo info;
		try
		{
			info = Image.Identify(stream);
		}
		catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
		{
			throw new InvalidDataException($"Texture '{name}' is not a supported image: {ex.Message}", ex);
		}

		return new NullTexture2D(name, (uint)info.Width, (uint)info.Height);
	}
}
