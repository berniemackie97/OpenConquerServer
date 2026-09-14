using System.Buffers;
using System.Text;

namespace OpenConquer.Domain.Characters;

public static class CharacterNamePolicy
{
    public const int MinimumEncodedLength = 4;
    public const int MaximumEncodedLength = 15;

    private static readonly SearchValues<char> s_disallowedCharacters = SearchValues.Create(" ;,/\\=%@'\"[]");
    private static readonly Encoding s_encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) ?? throw new InvalidOperationException("Windows-1252 encoding is unavailable.");

    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.AsSpan().ContainsAny(s_disallowedCharacters))
        {
            return false;
        }

        foreach (char character in name)
        {
            if (character < ' ')
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
