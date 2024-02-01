using WebGPU;

namespace Ion.Extensions.Graphics;

public class Texture2D(WGPUTexture texture, WGPUTextureDescriptor textureDescriptor) : BaseTexture(texture, textureDescriptor)
{
	public static implicit operator WGPUTexture(Texture2D texture) => texture._texture;
}

//public static class TextureFactoryExtensions
//{
//	public static Texture2D CreateTexture2D(this IGraphicsContext graphics, in WGPUTextureDescriptor textureDescriptor)
//	{
//		var texture = graphics.Device.CreateTexture(in textureDescriptor);
//		return new Texture2D(texture);
//	}
//}
