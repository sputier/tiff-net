namespace Eleris.TiffNet;

/// <summary>
/// Data that is not valid TIFF: a bad structure, truncated or damaged data, or a limit of the reading options exceeded.
/// </summary>
/// <remarks>
/// A damaged or hostile file fails with this exception or with a <see cref="NotSupportedException"/>, never with an index,
/// overflow or out-of-memory error. A failure of the caller's own file or stream is not a <see cref="TiffException"/>: its
/// exception is rethrown as it is.
/// </remarks>
public sealed class TiffException : Exception
{
    /// <summary>Creates an exception for invalid data.</summary>
    public TiffException()
    {
    }

    /// <summary>Creates an exception for invalid data.</summary>
    /// <param name="message">What is wrong, naming the file by its file name only.</param>
    public TiffException(string message) : base(message)
    {
    }

    /// <summary>Creates an exception for invalid data revealed by another error.</summary>
    /// <param name="message">What is wrong, naming the file by its file name only.</param>
    /// <param name="innerException">The error that revealed it.</param>
    public TiffException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for a limit of the reading options that the data would exceed.</summary>
    /// <param name="message">What is wrong, naming the file by its file name only.</param>
    /// <param name="limit">The name of the limit, for example <c>MaxPixelBytes</c>.</param>
    public TiffException(string message, string limit) : base(message)
    {
        Limit = limit;
    }

    /// <summary>
    /// The name of the limit of the reading options that the data would exceed, for example <c>MaxPixelBytes</c>, or
    /// <see langword="null"/> when the data is damaged rather than too large.
    /// </summary>
    /// <remarks>Lets an application tell "too large for this device" from "damaged".</remarks>
    public string? Limit { get; }
}
