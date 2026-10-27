using RDCore.SDK.Model.Diagnostics;
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

    // an entry is a title and the page it links to, a line each: a line that is neither (two entries run together, `href: a.md- name: B`) is a toc docfx
    // cannot read, and the site is not built at all.
    [TestMethod]
    public void EveryLineOfTheTableOfContents_IsATitleOrAPage()
    {
        var malformed = File.ReadAllLines(Path.Combine(Diagnostics(), "toc.yml"))
            .Where(line => line.Length > 0 && !line.StartsWith("- name: ", StringComparison.Ordinal)
                && !System.Text.RegularExpressions.Regex.IsMatch(line, @"^  href: [A-Za-z0-9._-]+\.md$"))
            .ToArray();

        Assert.IsEmpty(malformed, $"not an entry of toc.yml: {string.Join(" | ", malformed)}");
    }

    // a row of the index names one code: two rows run together are one line the table shows as one.
    [TestMethod]
    public void EveryRowOfTheIndex_IsOneCode()
    {
        var merged = File.ReadAllLines(Path.Combine(Diagnostics(), "index.md"))
            .Where(line => System.Text.RegularExpressions.Regex.Matches(line, @"\|\[(VBC|VBR|VBA|RDC)[-0-9]+\]\(").Count > 1)
            .ToArray();

        Assert.IsEmpty(merged, $"rows run together in index.md: {string.Join(" | ", merged)}");
    }

    // a title written as it is is a plain YAML scalar, which ": " ends: one that has it (`Type mismatch: array or ...`) is a toc docfx cannot read, and
    // every link to a page of it a warning of an invalid link. Such a title is quoted.
    [TestMethod]
    public void EveryTitleOfTheTableOfContents_IsOneYamlReads()
    {
        var unreadable = File.ReadAllLines(Path.Combine(Diagnostics(), "toc.yml"))
            .Where(line => line.StartsWith("- name: ", StringComparison.Ordinal) && line["- name: ".Length..] is var title
                && !title.StartsWith('"') && (title.Contains(": ", StringComparison.Ordinal) || title.Contains(" #", StringComparison.Ordinal)))
            .ToArray();

        Assert.IsEmpty(unreadable, $"quote these titles in toc.yml: {string.Join(" | ", unreadable)}");
    }

    // an analyzer issues these: the number is the code the page is named by, and the one editors of older builds link to.
    private static readonly (RDCoreDiagnosticId Id, int Code)[] Published =
    [
        (RDCoreDiagnosticId.ImplicitDeclarationsEnabled, 101),
        (RDCoreDiagnosticId.ImplicitNonDefaultArrayBase, 102),
        (RDCoreDiagnosticId.ImplicitTypeDeclarationsEnabled, 103),
        (RDCoreDiagnosticId.ImplicitByRefModifier, 104),
        (RDCoreDiagnosticId.ImplicitPublicMember, 105),
        (RDCoreDiagnosticId.ImplicitVariantDeclaration, 106),
        (RDCoreDiagnosticId.ImplicitVariantReturnType, 107),
        (RDCoreDiagnosticId.IntegerDataTypeDeclaration, 201),
        (RDCoreDiagnosticId.ModuleScopeDimDeclaration, 202),
        (RDCoreDiagnosticId.MultilineParameterDeclaration, 203),
        (RDCoreDiagnosticId.MultipleDeclarations, 204),
        (RDCoreDiagnosticId.MisleadingByRefParameter, 205),
        (RDCoreDiagnosticId.NotAllPathsReturnValue, 206),
        (RDCoreDiagnosticId.IsMissingAlwaysFalse, 207),
        (RDCoreDiagnosticId.ObsoleteCallStatement, 302),
        (RDCoreDiagnosticId.ObsoleteCommentSyntax, 303),
        (RDCoreDiagnosticId.ObsoleteErrorSyntax, 304),
        (RDCoreDiagnosticId.ObsoleteGlobalModifier, 305),
        (RDCoreDiagnosticId.ObsoleteLetStatement, 306),
        (RDCoreDiagnosticId.ObsoleteTypeHint, 307),
        (RDCoreDiagnosticId.ObsoleteWhileWend, 308),
        (RDCoreDiagnosticId.ObsoleteOnLocalErrorStatement, 309),
        (RDCoreDiagnosticId.RedundantByValArgument, 310),
        (RDCoreDiagnosticId.EarlyBindingOpportunity, 311),
        (RDCoreDiagnosticId.ImplementationsShouldBePrivate, 405),
        (RDCoreDiagnosticId.UnmatchedEventHandler, 407),
        (RDCoreDiagnosticId.UseMeaningfulIdentifierNames, 1001),
        (RDCoreDiagnosticId.HungarianNotation, 1002),
        (RDCoreDiagnosticId.SetThroughACallFails, 11012),
        (RDCoreDiagnosticId.ArgumentTypedArithmeticOverflow, 11018),
        (RDCoreDiagnosticId.IgnoredTypeCharacter, 11019),
        (RDCoreDiagnosticId.ProcedureConstantLength, 11021),
        (RDCoreDiagnosticId.ReDimOfAnImplicitVariant, 11022),
    ];

    [TestMethod]
    public void EveryPublishedRubberduckCoreDiagnostic_KeepsItsCode()
    {
        var renumbered = Published.Where(published => (int)published.Id != published.Code).Select(published => $"{published.Id} is {(int)published.Id}, published as {published.Code}").ToArray();

        Assert.IsEmpty(renumbered, $"a published code is never renumbered: {string.Join("; ", renumbered)}");
    }

    [TestMethod]
    public void EveryPublishedRubberduckCoreDiagnostic_HasAPage()
    {
        var missing = Published
            .Select(published => $"rdc{published.Code:00000}.md")
            .Where(page => !File.Exists(Path.Combine(Diagnostics(), page)))
            .ToArray();

        Assert.IsEmpty(missing, $"no page for: {string.Join(", ", missing)}");
    }
}
