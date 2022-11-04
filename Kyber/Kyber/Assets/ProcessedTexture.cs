using Veldrid;

using VeldridTexture = Veldrid.Texture;

namespace Kyber.Assets;

internal class ProcessedTexture
{
	public PixelFormat Format { get; set; }
	public TextureType Type { get; set; }
	public uint Width { get; set; }
	public uint Height { get; set; }
	public uint Depth { get; set; }
	public uint MipLevels { get; set; }
	public uint ArrayLayers { get; set; }
	public byte[] TextureData { get; set; }

	public ProcessedTexture(
		PixelFormat format,
		TextureType type,
		uint width,
		uint height,
		uint depth,
		uint mipLevels,
		uint arrayLayers,
		byte[] textureData)
	{
		Format = format;
		Type = type;
		Width = width;
		Height = height;
		Depth = depth;
		MipLevels = mipLevels;
		ArrayLayers = arrayLayers;
		TextureData = textureData;
	}

	public unsafe VeldridTexture CreateDeviceTexture(GraphicsDevice gd, ResourceFactory rf, TextureUsage usage)
	{
		VeldridTexture texture = rf.CreateTexture(new TextureDescription(Width, Height, Depth, MipLevels, ArrayLayers, Format, usage, Type));

		VeldridTexture staging = rf.CreateTexture(new TextureDescription(Width, Height, Depth, MipLevels, ArrayLayers, Format, TextureUsage.Staging, Type));

		ulong offset = 0;
		fixed (byte* texDataPtr = &TextureData[0])
		{
			for (uint level = 0; level < MipLevels; level++)
			{
				uint mipWidth = _getDimension(Width, level);
				uint mipHeight = _getDimension(Height, level);
				uint mipDepth = _getDimension(Depth, level);
				uint subresourceSize = mipWidth * mipHeight * mipDepth * _getFormatSize(Format);

				for (uint layer = 0; layer < ArrayLayers; layer++)
				{
					gd.UpdateTexture(staging, (IntPtr)(texDataPtr + offset), subresourceSize, 0, 0, 0, mipWidth, mipHeight, mipDepth, level, layer);
					offset += subresourceSize;
				}
			}
		}

		CommandList cl = rf.CreateCommandList();
		cl.Begin();
		cl.CopyTexture(staging, texture);
		cl.End();
		gd.SubmitCommands(cl);

		return texture;
	}

	private static uint _getFormatSize(PixelFormat format)
	{
		return format switch
		{
			PixelFormat.R8_G8_B8_A8_UNorm => 4,
			PixelFormat.BC3_UNorm => 1,
			_ => throw new NotImplementedException(),
		};
	}

	private static uint _getDimension(uint largestLevelDimension, uint mipLevel)
	{
		uint ret = largestLevelDimension;
		for (uint i = 0; i < mipLevel; i++)
		{
			ret /= 2;
		}

		return Math.Max(1, ret);
	}
}