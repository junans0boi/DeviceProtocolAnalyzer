using System.Globalization;
using System.Text;

namespace DeviceProtocolAnalyzer.Services;

public static class ByteFormatter
{
    public static string ToHex(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(data.Length * 3 - 1);
        for (var index = 0; index < data.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            builder.Append(data[index].ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    public static string ToDisplayText(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder();
        foreach (var value in data)
        {
            if (value is >= 0x20 and <= 0x7E)
            {
                builder.Append((char)value);
                continue;
            }

            builder.Append(value switch
            {
                0x00 => "<NUL>",
                0x01 => "<SOH>",
                0x02 => "<STX>",
                0x03 => "<ETX>",
                0x04 => "<EOT>",
                0x05 => "<ENQ>",
                0x06 => "<ACK>",
                0x07 => "<BEL>",
                0x08 => "<BS>",
                0x09 => "<TAB>",
                0x0A => "<LF>",
                0x0B => "<VT>",
                0x0C => "<FF>",
                0x0D => "<CR>",
                0x10 => "<DLE>",
                0x11 => "<DC1>",
                0x12 => "<DC2>",
                0x13 => "<DC3>",
                0x14 => "<DC4>",
                0x15 => "<NAK>",
                0x1B => "<ESC>",
                0x7F => "<DEL>",
                _ => ".",
            });
        }

        return builder.ToString();
    }
}
