using RDCore.SDK.Model.Errors;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// A code has a page the moment the platform can emit it, because the page is where the link of its diagnostic goes: a code without one is a dead link in every editor.
/// </summary>
[TestClass]
public sealed class DiagnosticDocumentationTests
{
    // codes that are specified and not issued by anything yet: the page comes with the first thing that raises one.
    private static readonly VBCompileErrorId[] NotIssued =
    [
        VBCompileErrorId.UnspecifiedCompileError,
        VBCompileErrorId.ForbiddenWithOptionStrict,
        VBCompileErrorId.InvalidParamArrayUse,
        VBCompileErrorId.InvalidReDim,
        VBCompileErrorId.ExpectedArray,
        VBCompileErrorId.ExpectedIdentifier,
    ];

    private static string Diagnostics()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "the repository root, where RDCore.slnx is");
        return Path.Combine(directory.FullName, "docs", "diagnostics");
    }

    [TestMethod]
    public void EveryCompileErrorTheStaticSemanticsCanRaise_HasAPage()
    {
        var missing = Enum.GetValues<VBCompileErrorId>()
            .Where(id => !NotIssued.Contains(id))
            .Select(id => $"vbc{(int)id:00000}.md")
            .Where(page => !File.Exists(Path.Combine(Diagnostics(), page)))
            .ToArray();

        Assert.IsEmpty(missing, $"no page for: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void EveryRuntimeErrorThePlatformCanRaise_HasAPage()
    {
        var missing = Enum.GetValues<VBRuntimeErrorId>()
            .Where(id => id != VBRuntimeErrorId.None && VBRuntimeErrorInfo.VBRuntimeErrors.ContainsKey(id))
            .Select(id => (int)id < 0 ? $"vbr-{Math.Abs((int)id):0000}.md" : $"vbr{(int)id:00000}.md")
            .Where(page => !File.Exists(Path.Combine(Diagnostics(), page)))
            .ToArray();

        Assert.IsEmpty(missing, $"no page for: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void EveryPage_IsInTheTableOfContents()
    {
        var toc = File.ReadAllText(Path.Combine(Diagnostics(), "toc.yml"));
        var missing = Directory.GetFiles(Diagnostics(), "*.md")
            .Select(Path.GetFileName)
            .Where(page => page != "index.md" && !toc.Contains($"href: {page}", StringComparison.Ordinal))
            .ToArray();

        Assert.IsEmpty(missing, $"not in toc.yml: {string.Join(", ", missing)}");
    }
}
