namespace Kyber;

public class KyberException : Exception {
    /// <summary>
    /// Constructs a new KyberException with the given message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public KyberException(string message) : base(message) { }

    /// <summary>
    /// Constructs a new KyberException with the given message and inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public KyberException(string message, Exception innerException) : base(message, innerException) { }
}
