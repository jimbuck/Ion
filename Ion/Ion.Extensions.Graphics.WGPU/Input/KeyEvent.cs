namespace Ion;

public partial struct KeyEvent {
    public Key Key;
    public bool Down;
    public ModifierKeys Modifiers;
    public bool Repeat;

    //internal KeyEvent(SDL.SDL_KeyboardEvent keyEvent)
    //{
    //    Key = (Key)keyEvent.;
    //    Down = keyEvent.Down;
    //    Modifiers = (ModifierKeys)keyEvent.Modifiers;
    //    Repeat = keyEvent.Repeat;
    //}

    //public static implicit operator KeyEvent(Veldrid.KeyEvent other) => new(other);

    //public override string ToString() => $"{Key} {(Down ? "Down" : "Up")} [{Modifiers}] (repeat={Repeat})";
}
