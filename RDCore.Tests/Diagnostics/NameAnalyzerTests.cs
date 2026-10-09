using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;
using System.Text;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The members that exist on another's account (<c>RDC00405</c>) and the names a module chooses (<c>RDC01001</c>, <c>RDC01002</c>).
/// </summary>
[TestClass]
public sealed class NameAnalyzerTests
{
    private static AnalyzerFinding[] Run(IModuleAnalyzer analyzer, string source)
    {
        var uri = TestUri.TestModuleUri();
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, parsed, null)).OrderBy(finding => finding.Range.Start)];
    }

    private static string[] Names(AnalyzerFinding[] findings) => [.. findings.Select(finding => finding.Message.Split('\'')[1])];

    private static IOptions<DiagnosticsOptions> Options(string[]? meaningful = null, string[]? hungarian = null)
        => Microsoft.Extensions.Options.Options.Create(new DiagnosticsOptions
        {
            MeaningfulNames = new NameRuleOptions { AllowedNames = meaningful ?? [] },
            HungarianNotation = new NameRuleOptions { AllowedNames = hungarian ?? [] },
        });

    #region RDC00405 implementations should be private

    private static readonly (string, string) Shape = ("IShape", ModuleWorkspace.ClassModule("IShape", "Public Sub Draw()", "End Sub", "Public Function Area() As Long", "End Function"));
    private static readonly (string, string) Button = ("Button", ModuleWorkspace.ClassModule("Button", "Public Event Click()"));

    private static async Task<AnalyzerFinding[]> ImplementationsAsync(params string[] lines)
    {
        var source = ModuleWorkspace.ClassModule("Implementer", ["Implements IShape", "Private WithEvents Source As Button", .. lines]);
        var payload = await ModuleWorkspace.SemanticsAsync([Shape, Button, ("Implementer", source)], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Implementer");

        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Implementer.cls"));
        return [.. new ImplementationsShouldBePrivateAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model))];
    }

    [TestMethod]
    public async Task APublicImplementationOfAnInterfaceMember_AndAPublicEventHandler_ShouldBePrivate()
    {
        var findings = await ImplementationsAsync(
            "Public Sub IShape_Draw()", "End Sub",
            "Public Function IShape_Area() As Long", "End Function",
            "Public Sub Source_Click()", "End Sub");

        CollectionAssert.AreEqual(new[] { "IShape_Draw", "IShape_Area", "Source_Click" }, Names(findings));
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.ImplementationsShouldBePrivate, Severity: DiagnosticSeverity.Information }));
        Assert.AreNotEqual(findings[0].Message.Replace("IShape_Draw", string.Empty), findings[2].Message.Replace("Source_Click", string.Empty), "an implementation is not a handler");
    }

    [TestMethod]
    public async Task AMemberThatStatesNoAccess_IsPublic_AndIsReportedToo()
        => CollectionAssert.AreEqual(new[] { "IShape_Draw" }, Names(await ImplementationsAsync("Sub IShape_Draw()", "End Sub")));

    [TestMethod]
    public async Task APrivateImplementation_AndAPublicMemberThatOnlyLooksLikeOne_AreFine()
        => Assert.IsEmpty(await ImplementationsAsync(
            "Private Sub IShape_Draw()", "End Sub",
            "Private Function IShape_Area() As Long", "End Function",
            "Private Sub Source_Click()", "End Sub",
            "Public Sub IShape_Helper()", "End Sub",
            "Public Sub Source_Other()", "End Sub"));

    [TestMethod]
    public void WithNoFactsFromTheHost_TheAnalyzerWillNotGuessFromAnUnderscore()
        => Assert.IsEmpty(Run(new ImplementationsShouldBePrivateAnalyzer(), "Implements IShape\nPublic Sub IShape_Draw()\nEnd Sub"));

    #endregion

    #region RDC01001 meaningful names

    private const string Declarations = """
        Private x As Long
        Private total1 As Long
        Private tmp As Long
        Private été As Long
        Private Total As Long
        Public Const PI2 = 3
        Public Sub Process(ByVal a As Long, ByVal count As Long)
            Dim i As Long
            Dim result As Long
        End Sub
        Public Sub Handler_Click()
        End Sub
        """;

    [TestMethod]
    public void ANameThatIsTooShort_EndsWithADigit_OrHasNoVowel_DoesNotSayWhatItIs()
    {
        var findings = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), Declarations);

        CollectionAssert.AreEqual(new[] { "x", "total1", "tmp", "PI2", "a", "i" }, Names(findings));
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.UseMeaningfulIdentifierNames, Severity: DiagnosticSeverity.Information }));
    }

    [TestMethod]
    public void EachRuleHasAMessageOfItsOwn()
    {
        var messages = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), Declarations)
            .ToDictionary(finding => finding.Message.Split('\'')[1], finding => finding.Message.Replace($"'{finding.Message.Split('\'')[1]}'", "''"));

        Assert.AreNotEqual(messages["x"], messages["total1"]);
        Assert.AreNotEqual(messages["total1"], messages["tmp"]);
        Assert.AreNotEqual(messages["x"], messages["tmp"]);
        Assert.AreEqual(messages["x"], messages["a"], "the same rule, the same message");
    }

    [TestMethod]
    public void AccentedVowels_AreVowels()
        => Assert.IsFalse(Names(Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), Declarations)).Contains("été"));

    [TestMethod]
    public void AnApprovedName_IsNotReported_WhateverItsCase()
    {
        var findings = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options(meaningful: ["I", "X", "tmp"])), Declarations);

        CollectionAssert.AreEqual(new[] { "total1", "PI2", "a" }, Names(findings));
    }

    [TestMethod]
    public void TheNameOfAnImplementationOrAHandler_IsTheInterfaceOrTheEventToChoose()
        => Assert.IsFalse(Names(Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), Declarations)).Contains("Handler_Click"));

    [TestMethod]
    public void TheAccessorsOfAProperty_AreOneName()
    {
        var findings = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()),
            "Public Property Get Zz() As Long\nEnd Property\nPublic Property Let Zz(ByVal value As Long)\nEnd Property");

        CollectionAssert.AreEqual(new[] { "Zz" }, Names(findings));
    }

    [TestMethod]
    public void TheNamesOfTheMembersOfAnEnumAndAType_AreChosenByTheModule()
    {
        var findings = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), "Enum Colors\n    Red\n    B\nEnd Enum\nType Pt\n    X As Long\n    Label As String\nEnd Type");

        CollectionAssert.AreEqual(new[] { "B", "Pt", "X" }, Names(findings));
    }

    #endregion

    #region RDC01002 Hungarian notation

    private const string Prefixed = """
        Private strName As String
        Private lngCount As Long
        Private stringent As Boolean
        Private strategy As Long
        Private colWidth As Long
        Private rsData As Object
        Private Str As String
        Public Sub Work(ByVal intValue As Long, ByVal valueInt As Long)
            Dim dblRate As Double
        End Sub
        """;

    [TestMethod]
    public void ANameThatBeginsWithAPrefixOfItsType_AndThenAWord_IsHungarian()
    {
        var findings = Run(new HungarianNotationAnalyzer(Options()), Prefixed);

        CollectionAssert.AreEqual(new[] { "strName", "lngCount", "colWidth", "rsData", "intValue", "dblRate" }, Names(findings));
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.HungarianNotation, Severity: DiagnosticSeverity.Information }));
    }

    [TestMethod]
    public void AnApprovedName_IsNotHungarian()
        => CollectionAssert.AreEqual(
            new[] { "strName", "lngCount", "rsData", "intValue", "dblRate" },
            Names(Run(new HungarianNotationAnalyzer(Options(hungarian: ["COLWIDTH"])), Prefixed)));

    [TestMethod]
    [DataRow("strName", true)]
    [DataRow("str", false)]
    [DataRow("stringent", false)]
    [DataRow("Str", false)]
    [DataRow("strName1", true)]
    [DataRow("nameStr", false)]
    public void TheRule_IsAPrefixAndThenANewWord(string name, bool hungarian)
        => Assert.AreEqual(hungarian, HungarianNotationAnalyzer.IsHungarian(name));

    #endregion

    #region configuration

    private static DiagnosticsOptions Bind(string json)
        => new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build()
            .GetSection("Configuration:Diagnostics").Get<DiagnosticsOptions>()!;

    [TestMethod]
    public void TheAllowLists_AreBoundFromTheDiagnosticsSection()
    {
        var options = Bind("""
            { "Configuration": { "Diagnostics": {
                "MeaningfulNames": { "AllowedNames": [ "i", "j" ] },
                "HungarianNotation": { "AllowedNames": [ "colWidth" ] } } } }
            """);

        CollectionAssert.AreEqual(new[] { "i", "j" }, options.MeaningfulNames.AllowedNames);
        CollectionAssert.AreEqual(new[] { "colWidth" }, options.HungarianNotation.AllowedNames);
    }

    [TestMethod]
    public void WithNothingConfigured_NoNameIsApproved()
    {
        var options = new DiagnosticsOptions();

        Assert.IsEmpty(options.MeaningfulNames.AllowedNames);
        Assert.IsEmpty(options.HungarianNotation.AllowedNames);
    }

    [TestMethod]
    public void TheSettingsFileTheExtensionShips_IsValid_AndApprovesNothing()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory);
        var shipped = new ConfigurationBuilder().AddJsonFile(Path.Combine(directory.FullName, "RDCore.Diagnostics", "appsettings.json")).Build()
            .GetSection("Configuration:Diagnostics").Get<DiagnosticsOptions>();

        Assert.IsNotNull(shipped);
        Assert.IsEmpty(shipped.MeaningfulNames.AllowedNames);
        Assert.IsEmpty(shipped.HungarianNotation.AllowedNames);
    }

    #endregion
}
