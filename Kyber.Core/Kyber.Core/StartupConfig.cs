using Kyber.Core.Graphics;

namespace Kyber.Core;

public interface IStartupConfig
{
    #region Window Options

    string WindowTitle { get; }
    int? WindowHeight { get; }
    int? WindowWidth { get; }
    int? WindowX { get; }
    int? WindowY { get; }
    WindowState WindowState { get; }

    #endregion

    #region Graphics Options

    GraphicsBackend PreferredBackend { get; }


    #endregion

    #region Headless Options

    GraphicsOutput GraphicsOutput { get; }
    string? OutputFileDirectory { get; }
    string? OutputFileTemplate { get; }

    #endregion
}

public class StartupConfig : IStartupConfig
{
    public string WindowTitle { get; set; } = "Kyber";
    public int? WindowHeight { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set;  }
    public WindowState WindowState { get; set; } = WindowState.Normal;


    public GraphicsOutput GraphicsOutput { get; set; } = GraphicsOutput.Window;

    public GraphicsBackend PreferredBackend { get; set; } = GraphicsBackend.Unspecified;

    public string? OutputFileDirectory { get; set; }
    public string? OutputFileTemplate { get; set; }
}
