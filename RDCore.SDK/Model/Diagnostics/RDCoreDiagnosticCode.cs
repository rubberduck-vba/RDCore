using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Model.Diagnostics;

/// <summary>
/// The code a diagnostic is reported under: a prefix of three letters that says who issues it, and a number of five digits, as in <c>XYZ00042</c>
/// (<strong>RD-VBAL §1.1.4</strong>).
/// </summary>
/// <remarks>
/// The platform issues the <c>VBC</c>, <c>VBR</c> and <c>VBA</c> codes. Its first-party extensions issue the <c>RDC</c> codes, which
/// <see cref="RDCoreDiagnosticId"/> names, and the <c>RDX</c> codes. An extension of another publisher reports its own diagnostics under the prefix its
/// manifest declares (<see cref="Extensibility.ExtensionInfo.DiagnosticPrefix"/>), which no other started extension declares.
/// </remarks>
public readonly record struct RDCoreDiagnosticCode
{
    /// <summary>
    /// The prefix of the codes of the platform's syntax and compile errors.
    /// </summary>
    public const string CompilePrefix = "VBC";

    /// <summary>
    /// The prefix of the codes of the run-time errors of the language.
    /// </summary>
    public const string RuntimePrefix = "VBR";

    /// <summary>
    /// The prefix of the codes of the errors a program raises itself.
    /// </summary>
    public const string ApplicationPrefix = "VBA";

    /// <summary>
    /// The prefix of the codes of the core diagnostics (<see cref="RDCoreDiagnosticId"/>).
    /// </summary>
    public const string CorePrefix = "RDC";

    /// <summary>
    /// The prefix of the codes of the diagnostics of the platform's other first-party extensions.
    /// </summary>
    public const string ExtensionPrefix = "RDX";

    /// <summary>
    /// The largest number a code has: five digits.
    /// </summary>
    public const int MaxNumber = 99999;

    /// <summary>
    /// The prefixes of the platform's own codes: the language's errors, and its first-party extensions' diagnostics.
    /// </summary>
    public static IReadOnlyList<string> FirstPartyPrefixes { get; } = [CompilePrefix, RuntimePrefix, ApplicationPrefix, CorePrefix, ExtensionPrefix];

    /// <summary>
    /// Creates the code of a prefix and a number.
    /// </summary>
    /// <param name="prefix">Three letters, <c>A</c> to <c>Z</c>.</param>
    /// <param name="number">A number from 0 to <see cref="MaxNumber"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> is not three letters <c>A</c> to <c>Z</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number"/> is not from 0 to <see cref="MaxNumber"/>.</exception>
    public RDCoreDiagnosticCode(string prefix, int number)
    {
        if (!IsPrefix(prefix))
        {
            throw new ArgumentException($"A diagnostic code prefix is three letters A to Z: '{prefix}' is not.", nameof(prefix));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(number);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, MaxNumber);
        (Prefix, Number) = (prefix, number);
    }

    /// <summary>
    /// The three letters that say who issues the diagnostic.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// The number of the diagnostic among those of <see cref="Prefix"/>.
    /// </summary>
    public int Number { get; }

    /// <summary>
    /// Whether the code is one of the platform's own (<see cref="FirstPartyPrefixes"/>), which its documentation describes.
    /// </summary>
    public bool IsFirstParty => Prefix is not null && IsFirstPartyPrefix(Prefix);

    /// <summary>
    /// Whether a text is a prefix of a diagnostic code: three letters <c>A</c> to <c>Z</c>.
    /// </summary>
    /// <param name="prefix">The text to check.</param>
    public static bool IsPrefix([NotNullWhen(true)] string? prefix)
        => prefix is { Length: 3 } && prefix.All(letter => letter is >= 'A' and <= 'Z');

    /// <summary>
    /// Whether a prefix is one of the platform's own (<see cref="FirstPartyPrefixes"/>).
    /// </summary>
    /// <param name="prefix">The prefix to check.</param>
    public static bool IsFirstPartyPrefix(string prefix) => FirstPartyPrefixes.Contains(prefix, StringComparer.Ordinal);

    /// <summary>
    /// Reads a code written as its prefix and its five digits, as in <c>XYZ00042</c>.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="code">The code read; <see langword="default"/> when <paramref name="text"/> is not one.</param>
    /// <returns>Whether <paramref name="text"/> is a code.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out RDCoreDiagnosticCode code)
    {
        code = default;
        if (text is not { Length: 8 } || !IsPrefix(text[..3]) || text.AsSpan(3).ContainsAnyExceptInRange('0', '9'))
        {
            return false;
        }

        code = new RDCoreDiagnosticCode(text[..3], int.Parse(text.AsSpan(3), System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>
    /// The code as it is written: its prefix, and its number in five digits.
    /// </summary>
    public override string ToString() => $"{Prefix}{Number:00000}";
}
