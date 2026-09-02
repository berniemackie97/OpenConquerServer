namespace OpenConquer.Protocol.Text;

/// <summary>
/// Identifies the text encodings currently established by Conquer Online 5517
/// protocol evidence.
/// </summary>
public enum TqTextEncoding
{
    /// <summary>
    /// Windows-1252 using the runtime's default code-page fallback behavior.
    /// </summary>
    Ansi,

    /// <summary>
    /// Windows-1252 using exception fallbacks.
    /// </summary>
    StrictAnsi,

    /// <summary>
    /// Seven-bit ASCII using the standard replacement fallback behavior.
    /// </summary>
    Ascii,
}
