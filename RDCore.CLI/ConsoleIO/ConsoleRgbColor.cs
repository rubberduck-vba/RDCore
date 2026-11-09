namespace RDCore.CLI.ConsoleIO;

// a frame needs the theme's exact 24-bit value (for the escape sequences that set the terminal's default colours) and its nearest legacy ConsoleColor
// (for a terminal that can do neither): this carries the first and computes the second, so neither the renderer nor the theme has to.
public readonly record struct ConsoleRgbColor(byte R, byte G, byte B)
{
    public static ConsoleRgbColor Black { get; } = new(0, 0, 0);

    public static bool TryParse(string? token, out ConsoleRgbColor color)
    {
        color = Black;
        if (token is null)
        {
            return false;
        }

        var digits = token.AsSpan().Trim().TrimStart('#');
        if (digits.Length == 3)
        {
            // #rgb is the #rrggbb shorthand: each digit is doubled, so #0af is #00aaff.
            Span<char> expanded = [digits[0], digits[0], digits[1], digits[1], digits[2], digits[2]];
            return TryParseSixDigits(expanded, out color);
        }

        return digits.Length == 6 && TryParseSixDigits(digits, out color);
    }

    private static bool TryParseSixDigits(ReadOnlySpan<char> digits, out ConsoleRgbColor color)
    {
        color = Black;
        if (!byte.TryParse(digits[..2], System.Globalization.NumberStyles.HexNumber, null, out var r)
            || !byte.TryParse(digits[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g)
            || !byte.TryParse(digits[4..], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = new ConsoleRgbColor(r, g, b);
        return true;
    }

    // the legacy 16-colour console palette, in ConsoleColor order.
    private static readonly (ConsoleColor Color, byte R, byte G, byte B)[] _consolePalette =
    [
        (ConsoleColor.Black, 0, 0, 0), (ConsoleColor.DarkBlue, 0, 0, 128), (ConsoleColor.DarkGreen, 0, 128, 0),
        (ConsoleColor.DarkCyan, 0, 128, 128), (ConsoleColor.DarkRed, 128, 0, 0), (ConsoleColor.DarkMagenta, 128, 0, 128),
        (ConsoleColor.DarkYellow, 128, 128, 0), (ConsoleColor.Gray, 192, 192, 192), (ConsoleColor.DarkGray, 128, 128, 128),
        (ConsoleColor.Blue, 0, 0, 255), (ConsoleColor.Green, 0, 255, 0), (ConsoleColor.Cyan, 0, 255, 255),
        (ConsoleColor.Red, 255, 0, 0), (ConsoleColor.Magenta, 255, 0, 255), (ConsoleColor.Yellow, 255, 255, 0),
        (ConsoleColor.White, 255, 255, 255),
    ];

    public ConsoleColor ToNearestConsoleColor()
    {
        var best = ConsoleColor.Black;
        var bestDistance = int.MaxValue;
        foreach (var (color, r, g, b) in _consolePalette)
        {
            var dr = R - r;
            var dg = G - g;
            var db = B - b;
            var distance = (dr * dr) + (dg * dg) + (db * db);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = color;
            }
        }
        return best;
    }

    public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";
}
