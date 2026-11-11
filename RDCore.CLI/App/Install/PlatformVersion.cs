using System.Diagnostics.CodeAnalysis;

namespace RDCore.CLI.App.Install;

/// <summary>
/// A platform version, as <c>rdcore.json</c>'s <c>platformVersion</c> and a release tag without its leading <c>v</c> name it: <c>0.1.0</c>,
/// <c>0.2.0-alpha.3</c>. Ordered as Semantic Versioning 2.0 orders versions: a prerelease comes before its release, and build metadata is
/// ignored.
/// </summary>
/// <remarks>
/// The same rules as the IDE add-in's platform installer, which deploys into the same folder: a version is read and ordered alike on both
/// sides, and its folder has the same name.
/// </remarks>
internal sealed record PlatformVersion : IComparable<PlatformVersion>
{
    private PlatformVersion(Version core, string[] prerelease, string text)
    {
        Core = core;
        Prerelease = prerelease;
        Text = text;
    }

    /// <summary>
    /// The major, minor and patch numbers.
    /// </summary>
    public Version Core { get; }

    /// <summary>
    /// The prerelease identifiers, such as <c>alpha</c> and <c>3</c>; empty for a release.
    /// </summary>
    public IReadOnlyList<string> Prerelease { get; }

    /// <summary>
    /// Whether this is a prerelease.
    /// </summary>
    public bool IsPrerelease => Prerelease.Count > 0;

    /// <summary>
    /// The version in lowercase, without a leading <c>v</c> or build metadata: <c>V0.1.0-RC.1+build.5</c> is <c>0.1.0-rc.1</c>. Its folder
    /// is named by it.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Reads a version, with or without a leading <c>v</c>; false when it is not one.
    /// </summary>
    /// <param name="text">The version's text, such as <c>0.2.0-alpha.3</c> or <c>v0.2.0</c>.</param>
    /// <param name="version">The version read; null when it is not one.</param>
    public static bool TryParse(string? text, [NotNullWhen(true)] out PlatformVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var plus = value.IndexOf('+');
        if (plus >= 0)
        {
            value = value[..plus];
        }

        // folders are named by the version, and Windows doesn't tell RC from rc: a version is case-insensitive, and written in lowercase.
        value = value.ToLowerInvariant();
        var dash = value.IndexOf('-');
        var core = dash >= 0 ? value[..dash] : value;
        var prerelease = dash >= 0 ? value[(dash + 1)..].Split('.') : [];
        if (prerelease.Any(identifier => identifier.Length == 0 || !identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            || HasLeadingZero(identifier)))
        {
            return false;
        }

        var numbers = core.Split('.');
        if (numbers.Length != 3 || !numbers.All(n => n.Length > 0 && n.All(char.IsAsciiDigit) && !HasLeadingZero(n)))
        {
            return false;
        }

        if (!Version.TryParse(core, out var parsed))
        {
            return false;
        }

        version = new PlatformVersion(parsed, prerelease, value);
        return true;
    }

    // Semantic Versioning forbids it in a number, so 01.0.0 and 1.0.0 can't be two versions, or one with two folders.
    private static bool HasLeadingZero(string identifier) =>
        identifier.Length > 1 && identifier[0] == '0' && identifier.All(char.IsAsciiDigit);

    public int CompareTo(PlatformVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byCore = Core.CompareTo(other.Core);
        if (byCore != 0)
        {
            return byCore;
        }

        if (IsPrerelease != other.IsPrerelease)
        {
            return IsPrerelease ? -1 : 1;
        }

        for (var i = 0; i < Math.Min(Prerelease.Count, other.Prerelease.Count); i++)
        {
            var byIdentifier = CompareIdentifiers(Prerelease[i], other.Prerelease[i]);
            if (byIdentifier != 0)
            {
                return byIdentifier;
            }
        }

        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    // numeric identifiers compare as numbers and come before alphanumeric ones, which compare in ASCII order.
    private static int CompareIdentifiers(string a, string b)
    {
        var aNumeric = a.All(char.IsAsciiDigit);
        var bNumeric = b.All(char.IsAsciiDigit);
        if (aNumeric && bNumeric)
        {
            return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
        }

        if (aNumeric != bNumeric)
        {
            return aNumeric ? -1 : 1;
        }

        return string.CompareOrdinal(a, b);
    }

    public bool Equals(PlatformVersion? other) => other is not null && CompareTo(other) == 0;

    public override int GetHashCode() => HashCode.Combine(Core, string.Join('.', Prerelease));

    public override string ToString() => Text;
}
