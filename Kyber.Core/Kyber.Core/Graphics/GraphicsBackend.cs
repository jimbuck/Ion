using VeldridGraphicsBackend = Veldrid.GraphicsBackend;

namespace Kyber.Core.Graphics;

public enum GraphicsBackend : byte
{
    Unspecified = 0,
    OpenGL,
    Direct3D11,
    Vulkan,
    Metal,
    OpenGLES,
}

internal static class GraphicsBackendExtensions {

    public static VeldridGraphicsBackend ToInternal(this GraphicsBackend backend)
    {
        return backend switch
        {
            GraphicsBackend.Direct3D11 => VeldridGraphicsBackend.Direct3D11,
            GraphicsBackend.Vulkan => VeldridGraphicsBackend.Vulkan,
            GraphicsBackend.OpenGL => VeldridGraphicsBackend.OpenGL,
            GraphicsBackend.Metal => VeldridGraphicsBackend.Metal,
            GraphicsBackend.OpenGLES => VeldridGraphicsBackend.OpenGLES,
            _ => throw new KyberException("Invalid GraphicsBackend: " + backend),
        };
    }
}

