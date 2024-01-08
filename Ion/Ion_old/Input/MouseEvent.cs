namespace Ion;

public struct MouseEvent
{
    public MouseButton MouseButton { get; }

    public bool Down { get; }

    internal MouseEvent(Veldrid.MouseEvent me)
    {
        MouseButton = (MouseButton)me.MouseButton;
        Down = me.Down;
    }

    public static implicit operator MouseEvent(Veldrid.MouseEvent other) => new(other);
}