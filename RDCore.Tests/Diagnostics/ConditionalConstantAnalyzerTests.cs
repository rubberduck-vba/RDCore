using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;
using static RDCore.Tests.Diagnostics.CodeActionFixture;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The constants a directive reads (<strong>MS-VBAL §3.4</strong>): <c>RDC01019</c>, a <c>Win64</c>, which is <c>1</c> on a 64-bit Mac too; and
/// <c>RDC01020</c>, a name nothing defines, which is <c>0</c>.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.4 Conditional Compilation")]
public sealed class ConditionalConstantAnalyzerTests
{
    // a directive is read off the module as it is written: no host is asked.
    private static AnalyzerFinding[] Win64Of(string lines)
    {
        var program = Module([.. lines.Split(" ;; "), "Public Sub Main()", "End Sub"]);
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new Win64IsABitnessTestAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), null))];
    }

    // what the installation sets and what the project defines are the host's to say: the module is analyzed as the host loads it.
    private static async Task<AnalyzerFinding[]> Win64OfTheHostAsync(string lines, bool win64OnlySetOnWindows, IReadOnlyDictionary<string, string>? constants)
    {
        var program = Module([.. lines.Split(" ;; "), "Public Sub Main()", "End Sub"]);
        var payload = await ModuleWorkspace.SemanticsAsync(
            [], program, environment: profile => profile with { Win64OnlySetOnWindows = win64OnlySetOnWindows }, constants: constants);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new Win64IsABitnessTestAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    // what is defined is the host's to say: the module is analyzed as the host loads it.
    private static async Task<AnalyzerFinding[]> UndefinedOfAsync(string lines)
    {
        var program = Module([.. lines.Split(" ;; "), "Public Sub Main()", "End Sub"]);
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new UndefinedConditionalConstantAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    // each fix changes which branch is compiled on some platform, which a client shows before it is made.
    private static string[] TitlesOf(AnalyzerFinding finding)
    {
        var fixes = finding.Fixes?.Edits ?? [];
        Assert.IsTrue(fixes.All(fix => fix.ChangesBehavior), "a fix changes which branch is compiled");
        return [.. fixes.Select(fix => fix.Title)];
    }

    [TestMethod]
    [DataRow("#If Win64 Then ;; #End If", DisplayName = "the condition of a #If")]
    [DataRow("#If Mac Then ;; #ElseIf Win64 Then ;; #End If", DisplayName = "the condition of a #ElseIf")]
    [DataRow("#Const Wide = Win64", DisplayName = "the value of a #Const")]
    [DataRow("#If False Then ;; #If Win64 Then ;; #End If ;; #End If", DisplayName = "a directive of a branch that is not live")]
    public void AWin64ThatADirectiveReads_IsAHint(string lines)
    {
        var found = Assert.ContainsSingle(Win64Of(lines));

        Assert.AreEqual(RDCoreDiagnosticId.Win64IsABitnessTest, found.Id);
        Assert.AreEqual(DiagnosticSeverity.Hint, found.Severity);
        Assert.Contains("Win64", found.Message);
        CollectionAssert.AreEqual(new[] { "Test Is64BitEnv", "Test Win32 And Is64BitEnv" }, TitlesOf(found));
    }

    [TestMethod]
    public void EachWin64_IsOne()
        => Assert.HasCount(2, Win64Of("#If Win64 Then ;; #ElseIf VBA7 And Win64 Then ;; #End If"));

    [TestMethod]
    [DataRow("#If Win32 Then ;; #End If", DisplayName = "another predefined constant")]
    [DataRow("#Const Win64 = 1 ;; #If Win64 Then ;; #End If", DisplayName = "a Win64 the module declares")]
    public void AWin64ThatMeansWhatItSays_IsNotOne(string lines)
        => Assert.IsEmpty(Win64Of(lines));

    [TestMethod]
    [DataRow(true, null, DisplayName = "Win64 set on Windows only by the installation")]
    [DataRow(false, "Win64", DisplayName = "a Win64 the project defines")]
    [DataRow(false, "WIN64", DisplayName = "a Win64 the project defines, in capitals")]
    public async Task AWin64TheHostSaysMeansWhatItSays_IsNotOne(bool win64OnlySetOnWindows, string? defined)
        => Assert.IsEmpty(await Win64OfTheHostAsync(
            "#If Win64 Then ;; #End If", win64OnlySetOnWindows, defined is null ? null : new Dictionary<string, string> { [defined] = "1" }));

    [TestMethod]
    public async Task AWin64_IsOne_WhereTheProjectDefinesOtherConstants()
        => Assert.ContainsSingle(await Win64OfTheHostAsync("#If Win64 And DEV Then ;; #End If", false, new Dictionary<string, string> { ["DEV"] = "1" }));

    [TestMethod]
    public void AWin64InCode_IsNotADirectivesConstant()
        => Assert.IsEmpty(Win64Of("Private Win64 As Long"));

    [TestMethod]
    public async Task TheFirstFix_TestsTheBitness()
        => await AssertFixedAsync(RDCoreDiagnosticId.Win64IsABitnessTest,
            ["Public Sub Main()", "#If Win64 Then", "    Debug.Print \"wide\"", "#End If", "End Sub"],
            ["Public Sub Main()", "#If Is64BitEnv Then", "    Debug.Print \"wide\"", "#End If", "End Sub"], runs: true, choice: 0);

    [TestMethod]
    public async Task TheSecondFix_Tests64BitWindows_InParenthesesWhereWin64IsAnOperand()
        => await AssertFixedAsync(RDCoreDiagnosticId.Win64IsABitnessTest,
            ["Public Sub Main()", "#If VBA7 And Win64 Then", "    Debug.Print \"wide\"", "#End If", "End Sub"],
            ["Public Sub Main()", "#If VBA7 And (Win32 And Is64BitEnv) Then", "    Debug.Print \"wide\"", "#End If", "End Sub"], runs: true, choice: 1);

    [TestMethod]
    public async Task TheSecondFix_NeedsNoParentheses_WhereWin64IsTheCondition()
        => await AssertFixedAsync(RDCoreDiagnosticId.Win64IsABitnessTest,
            ["Public Sub Main()", "#If Win64 Then", "    Debug.Print \"wide\"", "#End If", "End Sub"],
            ["Public Sub Main()", "#If Win32 And Is64BitEnv Then", "    Debug.Print \"wide\"", "#End If", "End Sub"], runs: true, choice: 1);

    [TestMethod]
    [DataRow("#If DEV Then ;; #End If", DisplayName = "the condition of a #If")]
    [DataRow("#If Mac Then ;; #ElseIf DEV Then ;; #End If", DisplayName = "the condition of a #ElseIf")]
    [DataRow("#Const Flag = DEV + 1", DisplayName = "the value of a #Const")]
    public async Task AConstantNothingDefines_IsAHint_WithNoFix(string lines)
    {
        var found = Assert.ContainsSingle(await UndefinedOfAsync(lines));

        Assert.AreEqual(RDCoreDiagnosticId.UndefinedConditionalConstant, found.Id);
        Assert.AreEqual(DiagnosticSeverity.Hint, found.Severity);
        Assert.Contains("DEV", found.Message);
        Assert.IsEmpty(TitlesOf(found));
    }

    [TestMethod]
    [DataRow("#Const DEV = 1 ;; #If DEV Then ;; #End If", DisplayName = "a #Const of the module")]
    [DataRow("#If DEV Then ;; #End If ;; #Const DEV = 1", DisplayName = "a #Const of the module, written after")]
    [DataRow("#If Win32 Or Win64 Or Mac Or Linux Or Is64BitEnv Or VBA6 Or VBA7 Or DEBUG Or Win16 Then ;; #End If", DisplayName = "the predefined constants")]
    public async Task ADefinedConstant_IsNotOne(string lines)
        => Assert.IsEmpty(await UndefinedOfAsync(lines));

    [TestMethod]
    public async Task EachRead_IsOne()
        => Assert.HasCount(2, await UndefinedOfAsync("#If DEV Then ;; #ElseIf Not dev Then ;; #End If"));

    [TestMethod]
    public async Task Linux64_IsRetired_AndNamesWhatTestsTheSameThing()
    {
        var found = Assert.ContainsSingle(await UndefinedOfAsync("#If Linux64 Then ;; #End If"));

        Assert.Contains("Linux And Is64BitEnv", found.Message);
        CollectionAssert.AreEqual(new[] { "Test Linux And Is64BitEnv" }, TitlesOf(found));
    }

    [TestMethod]
    public async Task TheFixOfLinux64_TestsLinuxAndTheBitness()
        => await AssertFixedAsync(RDCoreDiagnosticId.UndefinedConditionalConstant,
            ["Public Sub Main()", "#If Linux64 Then", "    Debug.Print \"linux\"", "#Else", "    Debug.Print \"other\"", "#End If", "End Sub"],
            ["Public Sub Main()", "#If Linux And Is64BitEnv Then", "    Debug.Print \"linux\"", "#Else", "    Debug.Print \"other\"", "#End If", "End Sub"],
            runs: true);

    [TestMethod]
    public async Task TheFixOfLinux64_IsInParentheses_WhereItIsAnOperand()
        => await AssertFixedAsync(RDCoreDiagnosticId.UndefinedConditionalConstant,
            ["Public Sub Main()", "#If VBA7 And Linux64 Then", "    Debug.Print \"linux\"", "#End If", "End Sub"],
            ["Public Sub Main()", "#If VBA7 And (Linux And Is64BitEnv) Then", "    Debug.Print \"linux\"", "#End If", "End Sub"], runs: true);
}
