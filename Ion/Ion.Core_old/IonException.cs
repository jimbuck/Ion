namespace Ion;

public class IonException : Exception
{
    /// <summary>
    /// Constructs a new IonException with the given message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public IonException(string message) : base(message) { }

    /// <summary>
    /// Constructs a new IonException with the given message and inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public IonException(string message, Exception innerException) : base(message, innerException) { }
}
