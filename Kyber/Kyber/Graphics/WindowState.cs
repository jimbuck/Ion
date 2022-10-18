namespace Kyber.Graphics;

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
    public static Veldrid.WindowState ToInternal(this WindowState windowState)
    {
        return windowState switch
        {
            WindowState.Normal => Veldrid.WindowState.Normal,
            WindowState.FullScreen => Veldrid.WindowState.FullScreen,
            WindowState.Maximized => Veldrid.WindowState.Maximized,
            WindowState.Minimized => Veldrid.WindowState.Minimized,
            WindowState.BorderlessFullScreen => Veldrid.WindowState.BorderlessFullScreen,
            WindowState.Hidden => Veldrid.WindowState.Hidden,
            _ => throw new KyberException("Invalid WindowState: " + windowState),
        };
    }
}
