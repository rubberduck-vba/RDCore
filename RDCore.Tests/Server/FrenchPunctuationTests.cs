using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RDCore.Tests.Server;

/// <summary>
/// The French of every <c>.fr.resx</c> in the repository is punctuated as in Québec (OQLF): a space before a colon, and none before a semicolon, a question
/// mark or an exclamation mark. The space is a plain one, as the translations write it; a non-breaking one is as good.
/// </summary>
[TestClass]
public sealed partial class FrenchPunctuationTests
{
    // where a colon is no punctuation: a format item ({0:N2}), a URL, a named argument (:=), a time (13:05), a drive (C:\), an inlay hint ([Prompt:]).
    [GeneratedRegex(@"\{[^{}]*:[^{}]*\}|[A-Za-z][A-Za-z0-9+.-]*://\S*|:=|\d:\d|\b[A-Za-z]:\\|\[\w+:\]")]
    private static partial Regex NotPunctuation();

    // a colon after anything but a space, the start of the text or the start of a line.
    [GeneratedRegex(@"(?<![ \u00A0\r\n]|^):")]
    private static partial Regex UnspacedColon();

    // a space before a semicolon, a question mark or an exclamation mark, other than a mark quoted as a symbol (« ! »).
    [GeneratedRegex(@"(?<!«)[ \u00A0][;?!](?! »)")]
    private static partial Regex SpacedMark();

    private static IEnumerable<(string File, string Name, string Value)> FrenchValues()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RDCore.slnx")))
        {
            root = root.Parent;
        }

        Assert.IsNotNull(root, "the repository root, where RDCore.slnx is");
        var excluded = new[] { "bin", "obj", "artifacts" };
        foreach (var file in Directory.EnumerateFiles(root.FullName, "*.fr.resx", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root.FullName, file);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // the stale RDCore/ folder is no project of the solution.
            if (parts.Any(excluded.Contains) || parts[0] == "RDCore")
            {
                continue;
            }

            foreach (var data in XDocument.Load(file).Root!.Elements("data"))
            {
                yield return (relative, (string)data.Attribute("name")!, (string?)data.Element("value") ?? string.Empty);
            }
        }
    }

    private static string[] Offending(Regex rule)
        => [.. FrenchValues()
            .Where(entry => rule.IsMatch(NotPunctuation().Replace(entry.Value, string.Empty)))
            .Select(entry => $"{entry.File} {entry.Name}: {entry.Value}")];

    [TestMethod]
    public void TheRepository_HasFrenchTranslations()
        => Assert.IsNotEmpty(FrenchValues().ToArray());

    [TestMethod]
    public void AColon_HasASpaceBeforeIt()
    {
        var offending = Offending(UnspacedColon());
        Assert.IsEmpty(offending, string.Join(Environment.NewLine, offending));
    }

    [TestMethod]
    public void ASemicolonAQuestionMarkOrAnExclamationMark_HasNoSpaceBeforeIt()
    {
        var offending = Offending(SpacedMark());
        Assert.IsEmpty(offending, string.Join(Environment.NewLine, offending));
    }
}
