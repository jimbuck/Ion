using System.Runtime.CompilerServices;

namespace Kyber.Core;

public partial struct KeyEvent {
    public Key Key;
    public bool Down;
    public ModifierKeys Modifiers;
    public bool Repeat;

    internal KeyEvent(Veldrid.KeyEvent keyEvent)
    {
        Key = (Key)keyEvent.Key;
        Down = keyEvent.Down;
        Modifiers = (ModifierKeys)keyEvent.Modifiers;
        Repeat = keyEvent.Repeat;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator KeyEvent(Veldrid.KeyEvent other) => new(other);

    public override string ToString()
    {
        return string.Format("{0} {1} [{2}] (repeat={3})", Key, Down ? "Down" : "Up", Modifiers, Repeat);
    }
}
