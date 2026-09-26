using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering3D;

/// <summary>
/// Loads a cube map from six images in a folder (<c>Load&lt;ICubemap&gt;("Skybox")</c>): <c>px, nx, py, ny, pz, nz</c> or
/// <c>right, left, top, bottom, front, back</c>, as <c>.png</c> or <c>.jpg</c>, square and of one size. Faces map to the
/// cube layers +X, -X, +Y, -Y, +Z, -Z; the renderer samples them so that a camera looking down -Z (Ion's forward) sees
/// the front (+Z) image unmirrored, right on its right and top above. A full mip chain is built on the CPU (the PBR
/// shader reads blurrier mips for rougher reflections and the smallest for diffuse ambient).
/// </summary>
internal sealed class CubemapLoader(Renderer3D renderer, IPersistentStorage storage) : IAssetLoader<ICubemap>
{
	private static readonly string[][] FaceNames =
	[
		["px", "right", "posx"],
		["nx", "left", "negx"],
		["py", "top", "up", "posy"],
		["ny", "bottom", "down", "negy"],
		["pz", "front", "posz"],
		["nz", "back", "negz"],
	];

	private static readonly string[] Extensions = [".png", ".jpg", ".jpeg"];

	public Type AssetType { get; } = typeof(ICubemap);

	public ICubemap Load(string path)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		var faces = new byte[6][];
		var size = 0;
		for (var face = 0; face < 6; face++)
		{
			var file = FindFace(path, face);
			using var stream = storage.Assets.Read(file);
			using var image = Image.Load<Rgba32>(stream);
			if (image.Width != image.Height) throw new InvalidDataException($"Cube map face '{file}' is {image.Width}x{image.Height}; faces must be square.");
			if (face == 0) size = image.Width;
			else if (image.Width != size) throw new InvalidDataException($"Cube map face '{file}' is {image.Width} texels wide; the first face is {size}.");
			faces[face] = new byte[size * size * 4];
			image.CopyPixelDataTo(faces[face]);
		}

		return Create(renderer, path, (uint)size, faces);
	}

	/// <summary>Creates a cube map from six RGBA8 faces (rows top to bottom) with a CPU mip chain.</summary>
	internal static Cubemap Create(Renderer3D renderer, string name, uint size, byte[][] faces)
	{
		if (faces.Length != 6) throw new ArgumentException("A cube map needs six faces.", nameof(faces));
		var levels = 1 + (int)Math.Floor(Math.Log2(Math.Max(1, size)));
		if (renderer.Device is not { } device) return new Cubemap(renderer, name, renderer.CreateTexturePlaceholder(), size);

		var texture = device.CreateTexture(new TextureDescriptor(size, size, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst,
			(uint)levels, Label: name, Dimension: TextureDimension.Cube));
		for (uint face = 0; face < 6; face++)
		{
			var level = faces[face];
			var width = (int)size;
			for (var mip = 0; mip < levels; mip++)
			{
				device.Queue.WriteTexture(texture, level, (uint)width * 4, new TextureRegion(0, 0, (uint)width, (uint)width, (uint)mip, face));
				if (mip + 1 < levels) level = Downsample(level, width);
				width = Math.Max(1, width / 2);
			}
		}

		return new Cubemap(renderer, name, renderer.CreateTexture(texture, ownsTexture: true), size);
	}

	/// <summary>A 2x2 box filter of a square RGBA8 image.</summary>
	internal static byte[] Downsample(byte[] source, int width)
	{
		var half = Math.Max(1, width / 2);
		var result = new byte[half * half * 4];
		for (var y = 0; y < half; y++)
		{
			var y0 = Math.Min(y * 2, width - 1);
			var y1 = Math.Min(y * 2 + 1, width - 1);
			for (var x = 0; x < half; x++)
			{
				var x0 = Math.Min(x * 2, width - 1);
				var x1 = Math.Min(x * 2 + 1, width - 1);
				for (var c = 0; c < 4; c++)
				{
					var sum = source[(y0 * width + x0) * 4 + c] + source[(y0 * width + x1) * 4 + c] + source[(y1 * width + x0) * 4 + c] + source[(y1 * width + x1) * 4 + c];
					result[(y * half + x) * 4 + c] = (byte)((sum + 2) >> 2);
				}
			}
		}

		return result;
	}

	private string FindFace(string folder, int face)
	{
		foreach (var name in FaceNames[face])
		{
			foreach (var extension in Extensions)
			{
				var file = folder.TrimEnd('/', '\\') + "/" + name + extension;
				if (File.Exists(storage.Assets.GetPath(file))) return file;
			}
		}

		throw new FileNotFoundException($"Cube map '{folder}' has no {FaceNames[face][0]} face (tried {string.Join(", ", FaceNames[face])} with {string.Join(", ", Extensions)}).", folder);
	}
}

/// <summary>A loaded cube map; releases its texture when disposed.</summary>
internal sealed class Cubemap(Renderer3D renderer, string name, TextureHandle handle, uint size) : ICubemap
{
	private static long _nextId;
	private bool _disposed;

	public nint Id { get; } = (nint)Interlocked.Increment(ref _nextId);

	public string Name { get; } = name;

	public TextureHandle Handle { get; } = handle;

	public uint Size { get; } = size;

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		renderer.DestroyTexture(Handle);
	}
}
