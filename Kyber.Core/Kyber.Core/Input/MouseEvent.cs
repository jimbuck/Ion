using System.Runtime.CompilerServices;

namespace Kyber.Core;

public struct MouseEvent
{
    public MouseButton MouseButton { get; }

    public bool Down { get; }

    internal MouseEvent(Veldrid.MouseEvent me)
    {
        MouseButton = (MouseButton)me.MouseButton;
        Down = me.Down;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MouseEvent(Veldrid.MouseEvent other) => new(other);
}