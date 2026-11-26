using System.Globalization;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Workspace;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>'@Ignore</c> and <c>'@IgnoreModule</c>: the findings the language server drops once every provider has answered, in the module the annotation is
/// written in, and <c>RDC00418</c> for an argument that names nothing they can suppress.
/// </summary>
[TestClass]
public sealed class IgnoreAnnotationsTests
{
    private static readonly Uri File = new("file:///c:/ws/Module1.bas");

    private static readonly HashSet<RDCoreDiagnosticCode> NoneDisabled = [];

    private const string NamesNoDiagnostic = "RDC00418";

    private static ModuleParseResult Parse(params string[] lines)
    {
        var parse = new ModuleParser().Parse(File, string.Join("\r\n", lines) + "\r\n");
        Assert.IsEmpty(parse.SyntaxErrors, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return parse;
    }

    // a finding of a code on a line, as a provider reports it.
    private static Diagnostic Finding(string code, int line) => new()
    {
        Code = new DiagnosticCode(code),
        Source = "RDCore",
        Message = code,
        Severity = DiagnosticSeverity.Information,
        Range = new Range(new Position(line, 0), new Position(line, 4)),
    };

    private static string[] Published(ModuleParseResult parse, AnalysisPhase phase = AnalysisPhase.All, IReadOnlySet<RDCoreDiagnosticCode>? disabled = null,
        params Diagnostic[] found)
        => [.. IgnoreAnnotations.Apply(File, parse, found, phase, disabled ?? NoneDisabled)
            .Select(diagnostic => $"{diagnostic.Code!.Value.String}@{diagnostic.Range.Start.Line}")];

    private static Diagnostic[] Told(ModuleParseResult parse)
        => [.. IgnoreAnnotations.Apply(File, parse, [], AnalysisPhase.All, NoneDisabled).Where(diagnostic => diagnostic.Code?.String == NamesNoDiagnostic)];

    // in a file of one module, either annotation suppresses the codes it names in the whole file, wherever it is written; other codes are published.
    [TestMethod]
    [DataRow("'@Ignore RDC01001", DisplayName = "'@Ignore, in the declarations section")]
    [DataRow("'@IgnoreModule RDC01001", DisplayName = "'@IgnoreModule, in the declarations section")]
    [DataRow("'@IgnoreModule(RDC01001)", DisplayName = "'@IgnoreModule, its argument in parentheses")]
    public void InAFileOfOneModule_TheCodeNamed_IsSuppressedInTheWholeFile(string annotation)
    {
        var parse = Parse(annotation, "Option Explicit", "Public Sub A()", "End Sub", "Public Sub B()", "End Sub");

        CollectionAssert.AreEqual(new[] { "RDC01002@2" }, Published(parse, found: [Finding("RDC01001", 2), Finding("RDC01002", 2), Finding("RDC01001", 4)]));
    }

    [TestMethod]
    public void AnAnnotationBelowAProcedure_StillSuppressesInTheWholeFile()
    {
        var parse = Parse("Option Explicit", "Public Sub A()", "End Sub", "'@Ignore RDC01001", "Public Sub B()", "End Sub");

        CollectionAssert.AreEqual(Array.Empty<string>(), Published(parse, found: [Finding("RDC01001", 1), Finding("RDC01001", 4)]));
    }

    [TestMethod]
    public void SeveralCodes_AreEachSuppressed()
        => CollectionAssert.AreEqual(new[] { "RDC01003@1" },
            Published(Parse("'@Ignore RDC01001, RDC01002", "Option Explicit"),
                found: [Finding("RDC01001", 1), Finding("RDC01002", 1), Finding("RDC01003", 1)]));

    // what a module written for legacy Rubberduck suppresses: the inspection's name stands for the codes that took its place, one or several.
    [TestMethod]
    public void ALegacyInspectionName_SuppressesTheCodesThatTookItsPlace()
    {
        Assert.IsTrue(LegacyInspectionNames.TryGetCodes("IsMissingOnInappropriateArgument", out var codes), "the alias the test is about");
        CollectionAssert.AreEquivalent(new[] { "RDC00207", "RDC00218" }, codes.Select(code => code.ToString()).ToArray());

        CollectionAssert.AreEqual(new[] { "RDC00101@1" }, Published(Parse("'@Ignore IsMissingOnInappropriateArgument", "Option Explicit"),
            found: [Finding("RDC00207", 1), Finding("RDC00218", 1), Finding("RDC00101", 1)]));
    }

    // an extension's code is written as a code: one the platform does not know is still a code, and suppresses that extension's findings.
    [TestMethod]
    public void AnExtensionsCode_IsSuppressed_AndIsNoArgumentThatNamesNothing()
    {
        var parse = Parse("'@IgnoreModule XYZ00042", "Option Explicit");

        CollectionAssert.AreEqual(Array.Empty<string>(), Published(parse, found: [Finding("XYZ00042", 1)]));
        Assert.IsEmpty(Told(parse));
    }

    // no argument: every code that can be disabled. A language error is published whatever the annotation says.
    [TestMethod]
    public void NoArgument_SuppressesEveryCodeThatCanBeDisabled_AndNoLanguageError()
        => CollectionAssert.AreEquivalent(new[] { "VBC01027@1", "VBR00011@1", "VBA00001@1" }, Published(Parse("'@IgnoreModule", "Option Explicit"),
            found: [Finding("RDC01001", 1), Finding("XYZ00042", 1), Finding("VBC01027", 1), Finding("VBR00011", 1), Finding("VBA00001", 1)]));

    [TestMethod]
    public void ALanguageErrorNamed_IsPublished_AndItsArgumentIsTold()
    {
        var parse = Parse("'@Ignore VBC01027", "Option Explicit");

        CollectionAssert.AreEqual(new[] { "VBC01027@1" }, Published(parse, AnalysisPhase.Runtime, found: [Finding("VBC01027", 1)]));
        var told = Assert.ContainsSingle(Told(parse));
        Assert.AreEqual(string.Format(CultureInfo.CurrentCulture, SuppressionMessagesOf("NamesALanguageError"), "Ignore", "VBC01027"), told.Message);
    }

    // RDC00418: a Warning at the argument, naming it; the annotation suppresses nothing for it, and what its other arguments name is suppressed.
    [TestMethod]
    public void AnArgumentThatNamesNothing_IsTold_AtTheArgument_AndSuppressesNothing()
    {
        var parse = Parse("'@Ignore Nonsense, RDC01001", "Option Explicit");

        var told = Assert.ContainsSingle(Told(parse));
        Assert.AreEqual(DiagnosticSeverity.Warning, told.Severity);
        Assert.AreEqual(new Range(new Position(0, "'@Ignore ".Length), new Position(0, "'@Ignore Nonsense".Length)), told.Range);
        Assert.AreEqual(string.Format(CultureInfo.CurrentCulture, SuppressionMessagesOf("NamesNoDiagnostic"), "Ignore", "Nonsense"), told.Message);
        CollectionAssert.AreEqual(new[] { "RDC01002@1", $"{NamesNoDiagnostic}@0" },
            Published(parse, found: [Finding("RDC01001", 1), Finding("RDC01002", 1)]));
    }

    // the static phase tells it, once: a client pushed to publishes the static phase before the runtime phase adds to it. Disabled, it is not told.
    [TestMethod]
    [DataRow(AnalysisPhase.Static, true)]
    [DataRow(AnalysisPhase.Runtime, false)]
    [DataRow(AnalysisPhase.All, true)]
    public void AnArgumentThatNamesNothing_IsToldByTheStaticPhase(AnalysisPhase phase, bool told)
    {
        var parse = Parse("'@Ignore Nonsense", "Option Explicit");

        Assert.AreEqual(told, Published(parse, phase).Contains($"{NamesNoDiagnostic}@0"));
        Assert.IsEmpty(Published(parse, phase, new HashSet<RDCoreDiagnosticCode> { IgnoreAnnotations.NamesNoDiagnostic }));
    }

    [TestMethod]
    public void ADocumentWithNoSuppression_IsPublishedAsFound()
    {
        Diagnostic[] found = [Finding("RDC01001", 1)];
        Assert.AreSame(found, IgnoreAnnotations.Apply(File, Parse("'@Folder(\"Parent\")", "Option Explicit"), found, AnalysisPhase.All, NoneDisabled));
    }

    // a file of blocks: '@IgnoreModule suppresses in the module it is in, a block's or the file's own; '@Ignore inside a block suppresses nothing, as its
    // form is the line above a block's header.
    [TestMethod]
    public void InAFileOfBlocks_IgnoreModule_SuppressesInItsOwnModule_AndIgnoreInNone()
    {
        var parse = Parse(
            "Public Sub Own()",     // 0
            "End Sub",
            "Class Account",        // 2
            "    '@IgnoreModule RDC01001",
            "    Public Sub Deposit()",
            "    End Sub",
            "End Class",
            "Module Logger",        // 7
            "    '@Ignore RDC01001",
            "    Public Sub Log()",
            "    End Sub",
            "End Module");
        Assert.HasCount(2, parse.BlockModules, "the premise: a file of two blocks");

        CollectionAssert.AreEqual(new[] { "RDC01001@0", "RDC01001@9" },
            Published(parse, found: [Finding("RDC01001", 0), Finding("RDC01001", 4), Finding("RDC01001", 9)]));
    }

    // the comment lines directly above a block's header are its module's (the SDK's binding): '@Ignore there suppresses in that block alone.
    [TestMethod]
    public void InAFileOfBlocks_IgnoreAboveABlocksHeader_SuppressesInThatBlock()
    {
        var parse = Parse(
            "Public Sub Own()",     // 0
            "End Sub",
            "'@Ignore RDC01001",    // 2
            "Class Account",
            "    Public Sub Deposit()",
            "    End Sub",
            "End Class",
            "Module Logger",        // 7
            "    Public Sub Log()",
            "    End Sub",
            "End Module");
        var account = new RDCoreDocument(File, parse).ParseOf(parse.BlockModules[0].Uri);
        Assert.HasCount(1, account!.GetHeaderAnnotations(), "the premise: the annotation is a header annotation of Account's module");

        CollectionAssert.AreEqual(new[] { "RDC01001@0", "RDC01001@8", "RDC01002@4" }, Published(parse,
            found: [Finding("RDC01001", 0), Finding("RDC01001", 4), Finding("RDC01001", 8), Finding("RDC01002", 4)]));
    }

    [TestMethod]
    public void InAFileOfBlocks_IgnoreModuleAboveABlocksHeader_SuppressesInThatBlock()
    {
        var parse = Parse(
            "Public Sub Own()",         // 0
            "End Sub",
            "'@IgnoreModule RDC01001",  // 2
            "Class Account",
            "    Public Sub Deposit()",
            "    End Sub",
            "End Class",
            "Module Logger",            // 7
            "    Public Sub Log()",
            "    End Sub",
            "End Module");
        var account = new RDCoreDocument(File, parse).ParseOf(parse.BlockModules[0].Uri);
        Assert.HasCount(1, account!.GetHeaderAnnotations(), "the premise: the annotation is on the lines Account's module is given");

        CollectionAssert.AreEqual(new[] { "RDC01001@0", "RDC01001@8", "RDC01002@4" }, Published(parse,
            found: [Finding("RDC01001", 0), Finding("RDC01001", 4), Finding("RDC01001", 8), Finding("RDC01002", 4)]));
    }

    [TestMethod]
    public void InAFileOfBlocks_IgnoreModuleOutsideEveryBlock_SuppressesInTheFilesOwnModule()
        => CollectionAssert.AreEqual(new[] { "RDC01001@4" }, Published(
            Parse("'@IgnoreModule RDC01001", "Public Sub Own()", "End Sub", "Class Account", "    Public Sub Deposit()", "    End Sub", "End Class"),
            found: [Finding("RDC01001", 1), Finding("RDC01001", 4)]));

    private static string SuppressionMessagesOf(string key)
        => new System.Resources.ResourceManager("RDCore.LanguageServer.Diagnostics.SuppressionMessages", typeof(IgnoreAnnotations).Assembly)
            .GetString(key, CultureInfo.CurrentUICulture)!;
}
