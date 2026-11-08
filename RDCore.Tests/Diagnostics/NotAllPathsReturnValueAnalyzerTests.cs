using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Semantics.Flow;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00206</c>: the host follows the code paths of each function and property getter when it loads the code, and the analyzer says what is worth saying about the
/// ones that do not assign the return value.
/// </summary>
[TestClass]
public sealed class NotAllPathsReturnValueAnalyzerTests
{
    private const string Source = """
        Attribute VB_Name = "Program"
        Option Explicit
        Public Function Always(ByVal c As Boolean) As Long
            If c Then
                Always = 1
            Else
                Always = 2
            End If
        End Function
        Public Function Sometimes(ByVal c As Boolean) As Long
            If c Then Sometimes = 1
        End Function
        Public Function Never(ByVal c As Boolean) As Long
            Dim x As Long
            x = 1
        End Function
        Public Function Stub(ByVal c As Boolean) As Long
        End Function
        Public Property Get Item(ByVal c As Boolean) As Long
            If c Then Exit Property
            Item = 1
        End Property
        Public Sub Work(ByVal c As Boolean)
            If c Then Exit Sub
        End Sub
        """;

    private static async Task<AnalyzerFinding[]> FindAsync(string source)
    {
        var uri = TestUri.TestModuleUri();
        var payload = await ModuleWorkspace.SemanticsAsync([], source.ReplaceLineEndings("\r\n") + "\r\n");

        return [.. new NotAllPathsReturnValueAnalyzer()
            .Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), payload.Modules.Single()))
            .OrderBy(finding => finding.Range.Start)];
    }

    [TestMethod]
    public async Task AFunctionOrGetterThatDoesNotAssignItsReturnValueOnAPath_IsAWarning_AtItsName()
    {
        var findings = await FindAsync(Source);

        CollectionAssert.AreEqual(new[] { 9, 12, 18 }, findings.Select(finding => finding.Range.Start.Line).ToArray(), "Sometimes, Never and the getter");
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.NotAllPathsReturnValue, Severity: DiagnosticSeverity.Warning }));
        Assert.AreEqual("Sometimes".Length, findings[0].Range.End.Character - findings[0].Range.Start.Character, "the name, and not the whole procedure");
    }

    [TestMethod]
    public async Task AFunctionThatNeverAssignsIt_IsToldApartFromOneThatSometimesDoes()
    {
        var findings = await FindAsync(Source);

        StringAssert.Contains(findings[0].Message, "'Sometimes'");
        StringAssert.Contains(findings[1].Message, "'Never'");
        Assert.AreNotEqual(
            findings[0].Message.Replace("Sometimes", string.Empty), findings[1].Message.Replace("Never", string.Empty), "never assigned is not the same as not on every path");
    }

    [TestMethod]
    public async Task AFunctionThatAssignsItOnEveryPath_AnEmptyBody_AndASub_AreNotReported()
    {
        var findings = await FindAsync(Source);

        Assert.IsFalse(findings.Any(finding => finding.Message.Contains("'Always'") || finding.Message.Contains("'Stub'") || finding.Message.Contains("'Work'")));
    }

    [TestMethod]
    public async Task WithNoFactsFromTheHost_TheAnalyzerWillNotGuess()
    {
        var uri = TestUri.TestModuleUri();
        var parsed = new ModuleParser().Parse(uri, Source);

        Assert.IsEmpty(new NotAllPathsReturnValueAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, null)));
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task ASub_ReturnsNoValue_AndTheHostStatesNothingOfIt()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], Source.ReplaceLineEndings("\r\n") + "\r\n");

        var work = payload.Modules.Single().Procedures.Single(procedure => procedure.Procedure.Fragment.EndsWith(".Work", StringComparison.Ordinal));
        Assert.IsNull(work.ReturnValue);
    }

    [TestMethod]
    public void TheFactTravelsTheWire()
    {
        var fact = new ReturnValueFact(ReturnValueAssignment.Sometimes, "Total", new SDK.Model.Source.SourceLocation(TestUri.TestModuleUri(), default), IsEmpty: false);
        var dto = new SDK.Platform.Protocol.ProcedureSemanticsDto(new Uri("file://rdcore-test#Mod1.Total"), true, [], [], fact);

        var back = SDK.Platform.Protocol.PlatformJson.Deserialize<SDK.Platform.Protocol.ProcedureSemanticsDto>(SDK.Platform.Protocol.PlatformJson.Serialize(dto));

        Assert.AreEqual(fact, back.ReturnValue);
    }
}
