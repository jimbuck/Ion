namespace Ion;

public struct MouseEvent
{
    public MouseButton MouseButton { get; }

    public bool Down { get; }

    //internal MouseEvent(SDL.SDL_MouseButtonEvent me)
    //{
    //    MouseButton = (MouseButton)me.type == SDL.SDL_EventType.mouse;
    //    Down = me.Down;
    //}

    //public static implicit operator MouseEvent(Veldrid.MouseEvent other) => new(other);
}