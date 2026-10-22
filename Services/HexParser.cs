using System.Globalization;

namespace DeviceProtocolAnalyzer.Services;

public static class HexParser
{
    private static readonly char[] Separators = [' ', '\t', '\r', '\n', ',', ';', ':', '-'];

    public static byte[] Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new FormatException("HEX data is empty.");
        }

        var hasSeparators = input.IndexOfAny(Separators) >= 0;
        if (!hasSeparators)
        {
            var compact = RemovePrefix(input.Trim());
            if (compact.Length == 0 || compact.Length % 2 != 0)
            {
                throw new FormatException("HEX data must contain complete two-digit bytes.");
            }

            var bytes = new byte[compact.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = ParseByte(compact.Substring(index * 2, 2));
            }

            return bytes;
        }

        var tokens = input.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            throw new FormatException("HEX data is empty.");
        }

        var result = new byte[tokens.Length];
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = RemovePrefix(tokens[index]);
            if (token.Length != 2)
            {
                throw new FormatException($"Invalid HEX byte '{tokens[index]}'.");
            }

            result[index] = ParseByte(token);
        }

        return result;
    }

    private static string RemovePrefix(string token)
    {
        return token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token;
    }

    private static byte ParseByte(string token)
    {
        if (!byte.TryParse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"Invalid HEX byte '{token}'.");
        }

        return value;
    }
}
