using Veldrid.StartupUtilities;

namespace Kyber.Graphics;

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

    public static Veldrid.GraphicsBackend ToInternal(this GraphicsBackend backend)
    {
        return backend switch
        {
            GraphicsBackend.Direct3D11 => Veldrid.GraphicsBackend.Direct3D11,
            GraphicsBackend.Vulkan => Veldrid.GraphicsBackend.Vulkan,
            GraphicsBackend.OpenGL => Veldrid.GraphicsBackend.OpenGL,
            GraphicsBackend.Metal => Veldrid.GraphicsBackend.Metal,
            GraphicsBackend.OpenGLES => Veldrid.GraphicsBackend.OpenGLES,
			GraphicsBackend.Unspecified => VeldridStartup.GetPlatformDefaultBackend(),
            _ => throw new KyberException("Invalid GraphicsBackend: " + backend),
        };
    }
}

