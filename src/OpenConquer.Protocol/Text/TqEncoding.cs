using System.Text;

namespace OpenConquer.Protocol.Text;

/// <summary>
/// Resolves protocol text selectors to their runtime encodings.
/// </summary>
internal static class TqEncoding
{
    private const int Windows1252CodePage = 1252;

    private static readonly Encoding s_ansi;
    private static readonly Encoding s_strictAnsi;

    static TqEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        s_ansi = Encoding.GetEncoding(Windows1252CodePage);
        s_strictAnsi = Encoding.GetEncoding(codepage: Windows1252CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    internal static Encoding Resolve(TqTextEncoding encoding)
    {
        return encoding switch
        {
            TqTextEncoding.Ansi => s_ansi,
            TqTextEncoding.StrictAnsi => s_strictAnsi,
            TqTextEncoding.Ascii => Encoding.ASCII,

            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown TQ text encoding."),
        };
    }
}
