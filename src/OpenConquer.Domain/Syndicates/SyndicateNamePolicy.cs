using System.Text;

namespace OpenConquer.Domain.Syndicates;

public static class SyndicateNamePolicy
{
    public const int MinimumEncodedLength = 1;
    public const int MaximumEncodedLength = 16;

    private static readonly Encoding s_encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                                                  ?? throw new InvalidOperationException("Windows-1252 encoding is unavailable.");

    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (char character in name)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        try
        {
            return s_encoding.GetByteCount(name) is >= MinimumEncodedLength and <= MaximumEncodedLength;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
