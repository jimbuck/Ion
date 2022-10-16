using VeldridWindowState = Veldrid.WindowState;

namespace Kyber.Core.Graphics;

public enum WindowState : byte
{
    Normal,
    FullScreen,
    Maximized,
    Minimized,
    BorderlessFullScreen,
    Hidden
}

internal static class WindowStateExtensions
{
    public static VeldridWindowState ToInternal(this WindowState windowState)
    {
        return windowState switch
        {
            WindowState.Normal => VeldridWindowState.Normal,
            WindowState.FullScreen => VeldridWindowState.FullScreen,
            WindowState.Maximized => VeldridWindowState.Maximized,
            WindowState.Minimized => VeldridWindowState.Minimized,
            WindowState.BorderlessFullScreen => VeldridWindowState.BorderlessFullScreen,
            WindowState.Hidden => VeldridWindowState.Hidden,
            _ => throw new KyberException("Invalid WindowState: " + windowState),
        };
    }
}
