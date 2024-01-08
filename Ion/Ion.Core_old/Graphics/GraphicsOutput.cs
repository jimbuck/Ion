namespace Ion.Graphics;

public enum GraphicsOutput: byte
{
    /// <summary>
    /// Indicates no graphical output should be generated. Useful for servers and unit tests.
    /// </summary>
    None = 0,

    /// <summary>
    /// Indicates that graphics will be rendered to a file. Useful for simulations and automated tests.
    /// </summary>
    File,

    /// <summary>
    /// Indicates that graphics will be rendered to a window. Default value for games.
    /// </summary>
    Window,
}

