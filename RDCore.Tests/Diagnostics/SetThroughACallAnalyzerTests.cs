using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11012</c>: a <c>Set</c> of a call that raises a run-time error whenever it runs, which MS-VBA compiles (MS-VBAL §5.4.3.9; observed in Excel):
/// reported at Error severity, not left to the run time.
/// </summary>
[TestClass]
public sealed class SetThroughACallAnalyzerTests
{
    // a class whose default member has a Property Set: what a Set of a call that returns one sets
    private static readonly (string, string) Settable = ("Settable", ModuleWorkspace.ClassModule("Settable",
        "Private mInner As Object",
        "Public Property Get Inner() As Object", "Attribute Inner.VB_UserMemId = 0", "    Set Inner = mInner", "End Property",
        "Public Property Set Inner(ByVal o As Object)", "    Set mInner = o", "End Property"));

    private static async Task<AnalyzerFinding[]> FindingsAsync(string declarations, string statement)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\n{declarations.Replace(" ;; ", "\r\n")}\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{statement}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([Settable], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new SetThroughACallAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("H", DisplayName = "s28 B25 a function of a class")]
    [DataRow("Créer", DisplayName = "a function named with an accented letter, named as its code names it")]
    public async Task ASetOfACallOfAClassWhoseDefaultMemberHasNoPropertySet_IsError438(string function)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(
            $"Function {function}() As Collection ;; Set {function} = New Collection ;; End Function", $"Set {function} = New Collection"));

        Assert.AreEqual(DiagnosticSeverity.Error, finding.Severity);
        StringAssert.Contains(finding.Message, "438");
        StringAssert.Contains(finding.Message, $"'{function}'");
    }

    [TestMethod]
    [DataRow("Function One() As Settable ;; Set One = New Settable ;; End Function", "Set One = New Collection", DisplayName = "s31 D01 its default member has a Property Set")]
    [DataRow("Function G() As Variant ;; End Function", "Set G = New Collection", DisplayName = "a Variant: what it returns is only known when it runs")]
    [DataRow("Function O() As Object ;; End Function", "Set O = New Collection", DisplayName = "an Object: so is what it returns (s31 D07)")]
    [DataRow("Private mItems As Collection ;; Property Get Items() As Collection ;; Set Items = mItems ;; End Property ;; Property Set Items(ByVal c As Collection) ;; Set mItems = c ;; End Property", "Set Items = New Collection", DisplayName = "a property that has a Property Set")]
    [DataRow("Function H() As Collection ;; Set H = New Collection ;; End Function", "Dim c As Collection ;; Set c = H", DisplayName = "a call that is no target")]
    public async Task ASetThatCanSucceed_IsNotReported(string declarations, string statement)
        => Assert.IsEmpty(await FindingsAsync(declarations, statement.Replace(" ;; ", "\r\n")));

    [TestMethod]
    public async Task ASetOfAFunctionsOwnNameInItsBody_IsItsResult()
        => Assert.IsEmpty(await FindingsAsync("Function H() As Collection ;; Set H = New Collection ;; End Function", "Debug.Print H.Count"));
}
