using System.Text;

namespace OpenConquer.Protocol.Text;

/// <summary>
/// Resolves protocol text selectors to their runtime encodings.
/// </summary>
internal static class WireTextEncodingResolver
{
    private const int Windows1252CodePage = 1252;

    private static readonly Encoding s_ansi;
    private static readonly Encoding s_strictAnsi;

    static WireTextEncodingResolver()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        s_ansi = Encoding.GetEncoding(Windows1252CodePage);
        s_strictAnsi = Encoding.GetEncoding(codepage: Windows1252CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    internal static Encoding Resolve(WireTextEncoding encoding)
    {
        return encoding switch
        {
            WireTextEncoding.Ansi => s_ansi,
            WireTextEncoding.StrictAnsi => s_strictAnsi,
            WireTextEncoding.Ascii => Encoding.ASCII,

            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown wire text encoding."),
        };
    }
}
