namespace RDCore.SDK.ConsoleIO;

/// <summary>
/// A 24-bit console colour, as a shell frame understands it: three channels, no style semantics, no
/// renderer dependency.
/// </summary>
/// <remarks>
/// The platform's themes are authored in 24-bit hex (<c>#0c0a50</c>), but a console frame needs both
/// that exact value (for the ANSI escape sequences that set the terminal's own default colours) and
/// its nearest legacy <see cref="ConsoleColor"/> (for a terminal that cannot do either). This type
/// carries the first and computes the second, so neither the renderer nor the theme layer has to.
/// </remarks>
/// <param name="R">The red channel.</param>
/// <param name="G">The green channel.</param>
/// <param name="B">The blue channel.</param>
public readonly record struct ConsoleRgbColor(byte R, byte G, byte B)
{
    /// <summary>Black — the value <see cref="TryParse"/> yields for an unparseable token.</summary>
    public static ConsoleRgbColor Black { get; } = new(0, 0, 0);

    /// <summary>
    /// Parses a <c>#rrggbb</c> or <c>#rgb</c> token (the leading <c>#</c> is optional).
    /// </summary>
    /// <param name="token">The colour token to parse.</param>
    /// <param name="color">The parsed colour; <see cref="Black"/> when parsing fails.</param>
    /// <returns><c>true</c> if <paramref name="token"/> is a hexadecimal colour literal.</returns>
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

    /// <summary>
    /// The nearest legacy <see cref="ConsoleColor"/>, by squared distance in RGB space — what a
    /// console that cannot render 24-bit colour gets instead.
    /// </summary>
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

    /// <summary>The <c>#rrggbb</c> form.</summary>
    public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";
}
