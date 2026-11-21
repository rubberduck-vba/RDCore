using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;
using static RDCore.Tests.Diagnostics.CodeActionFixture;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC01008</c>: a <c>Declare</c> that no <c>#If</c> on the platform constants makes dead, which calls a library on every platform; and a call of one
/// compiled where it is not declared, or everywhere. Every platform is considered, whichever the session runs on: a guard written for these tests is true on
/// each 64-bit platform, which is what a test runs on.
/// </summary>
[TestClass]
public sealed class NativeCallReachableOnEveryPlatformTests
{
    private const string Kernel32 = "Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long";

    // live on every 64-bit platform and on no 32-bit one: a procedure declared in it is declared in the session that runs the test, and not everywhere.
    private const string On64Bit = "#If Is64BitEnv Then";

    private static RDCoreDiagnosticCode Code => RDCoreDiagnosticId.NativeCallReachableOnEveryPlatform.ToCode();

    private static async Task<AnalyzerFinding[]> FoundAsync(params string[] lines)
        => [.. (await FindingsOfAsync(Module(lines))).Where(finding => finding.Code == Code)];

    [TestMethod]
    public async Task ADeclareOutsideAnyIf_IsAWarning()
    {
        var found = Assert.ContainsSingle(await FoundAsync(Kernel32));

        Assert.AreEqual(DiagnosticSeverity.Warning, found.Severity);
        Assert.Contains("GetTickCount", found.Message);
    }

    [TestMethod]
    [DataRow("#If Win32 Then", DisplayName = "Windows")]
    [DataRow("#If Win64 Then", DisplayName = "64-bit Windows and a 64-bit Mac")]
    [DataRow("#If Mac Then", DisplayName = "macOS")]
    [DataRow("#If Mac = 0 Then", DisplayName = "not on macOS")]
    public async Task ADeclareInAnIfOnThePlatformConstants_IsNotOne(string guard)
        => Assert.IsEmpty(await FoundAsync(guard, Kernel32, "#End If"));

    [TestMethod]
    public async Task ADeclareInEachBranchOfAnIfOnThePlatformConstants_IsNotOne()
        => Assert.IsEmpty(await FoundAsync(
            "#If Mac Then", "Private Declare PtrSafe Function Ticks Lib \"libc.dylib\" Alias \"clock\" () As Long",
            "#Else", "Private Declare PtrSafe Function Ticks Lib \"kernel32\" Alias \"GetTickCount\" () As Long", "#End If"));

    [TestMethod]
    public async Task ACallOutsideAnyIf_OfADeclareInEachBranchOfAnIfOnThePlatformConstants_IsNotOne()
        => Assert.IsEmpty(await FoundAsync(
            "#If Mac Then", "Private Declare PtrSafe Function Ticks Lib \"libc.dylib\" Alias \"clock\" () As Long",
            "#Else", "Private Declare PtrSafe Function Ticks Lib \"kernel32\" Alias \"GetTickCount\" () As Long", "#End If",
            "Public Sub Main()", "    Debug.Print Ticks()", "End Sub"));

    [TestMethod]
    public async Task ADeclareInEachBranchOfAnIfEveryPlatformMakesLiveTheFirstOf_IsOne_AtTheStatementOnEveryPlatform()
    {
        var found = Assert.ContainsSingle(await FoundAsync(
            "#If VBA7 Then", "Private Declare PtrSafe Function Ticks Lib \"kernel32\" Alias \"GetTickCount\" () As Long",
            "#Else", "Private Declare Function Ticks Lib \"kernel32\" Alias \"GetTickCount\" () As Long", "#End If"));

        Assert.AreEqual(2, found.Range.Start.Line, "the statement of the VBA7 branch");
    }

    [TestMethod]
    [DataRow("#If VBA7 Then", DisplayName = "VBA7, which every platform defines")]
    [DataRow("#If Win32 Or Mac Or Linux Then", DisplayName = "every platform's constant")]
    [DataRow("#If Not Mac Then", DisplayName = "Not of a predefined constant, which is -2 where it is 1")]
    public async Task ADeclareInAnIfEveryPlatformMakesLive_IsOne(string guard)
        => Assert.ContainsSingle(await FoundAsync(guard, Kernel32, "#End If"));

    [TestMethod]
    [DataRow("kernel32", "Win32", DisplayName = "a Windows library")]
    [DataRow("user32.dll", "Win32", DisplayName = "a Windows library, with its extension")]
    [DataRow("libc.dylib", "Mac", DisplayName = "a macOS library")]
    [DataRow("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", "Mac", DisplayName = "a macOS framework")]
    [DataRow("libc.so.6", "Linux", DisplayName = "a Linux library")]
    public async Task TheFix_WritesTheDeclareInAnIfOnThePlatformOfItsLibrary(string library, string constant)
        => await AssertFixedAsync(RDCoreDiagnosticId.NativeCallReachableOnEveryPlatform,
            [$"Private Declare PtrSafe Function F Lib \"{library}\" () As Long"],
            [$"#If {constant} Then", $"Private Declare PtrSafe Function F Lib \"{library}\" () As Long", "#End If"]);

    [TestMethod]
    public async Task TheFix_KeepsTheIndentationOfTheDeclare_AndItsLineContinuations()
        => await AssertFixedAsync(RDCoreDiagnosticId.NativeCallReachableOnEveryPlatform,
            ["    Private Declare PtrSafe Function GetTickCount _", "        Lib \"kernel32\" () As Long"],
            ["    #If Win32 Then", "    Private Declare PtrSafe Function GetTickCount _", "        Lib \"kernel32\" () As Long", "    #End If"]);

    [TestMethod]
    public async Task ADeclareThatSharesItsLine_IsOne_WithNoFix()
        => await AssertNoFixAsync(RDCoreDiagnosticId.NativeCallReachableOnEveryPlatform, $"Private x As Long: {Kernel32}");

    [TestMethod]
    public async Task ACallOutsideAnyIf_OfADeclareOutsideAnyIf_IsOneToo()
    {
        var found = await FoundAsync(Kernel32, "Public Sub Main()", "    Debug.Print GetTickCount()", "End Sub");

        Assert.HasCount(2, found);
        Assert.AreEqual(3, found[1].Range.Start.Line, "the call");
    }

    [TestMethod]
    public async Task ACallOutsideAnyIf_OfADeclareOnSomePlatforms_IsOne()
    {
        var found = Assert.ContainsSingle(await FoundAsync(
            On64Bit, Kernel32, "#End If", "Public Sub Main()", "    Debug.Print GetTickCount()", "End Sub"));

        Assert.AreEqual(5, found.Range.Start.Line, "the call");
        Assert.AreEqual(DiagnosticSeverity.Warning, found.Severity);
    }

    [TestMethod]
    public async Task ACallInTheIfOfItsDeclare_IsNotOne()
        => Assert.IsEmpty(await FoundAsync(
            On64Bit, Kernel32, "#End If", "Public Sub Main()", On64Bit, "    Debug.Print GetTickCount()", "#End If", "End Sub"));

    [TestMethod]
    public async Task ACallInAnIfOfAPlatform_OfADeclareOutsideAnyIf_IsNotOne()
    {
        var found = Assert.ContainsSingle(await FoundAsync(Kernel32, "Public Sub Main()", "#If Win32 Then", "    Debug.Print GetTickCount()", "#End If", "End Sub"));

        Assert.AreEqual(1, found.Range.Start.Line, "the Declare, and not the call");
    }

    [TestMethod]
    public async Task ACall_OfADeclareOfAnotherModule_IsOne_WhereItIsCompiledOnAPlatformTheOtherModuleDoesNotDeclareItOn()
    {
        var native = StandardModuleSource("Native", On64Bit, Kernel32.Replace("Private", "Public", StringComparison.Ordinal), "#End If");
        var program = Module("Public Sub Main()", "    Debug.Print GetTickCount()", "End Sub");

        var payload = await ModuleWorkspace.SemanticsAsync([], program, modules: [("Native", native)]);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        var found = new NativeCallReachableOnEveryPlatformAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model)).ToArray();

        Assert.AreEqual(2, Assert.ContainsSingle(found).Range.Start.Line, "the call");
    }

    private static string StandardModuleSource(string name, params string[] lines) => $"Attribute VB_Name = \"{name}\"\r\n{string.Join("\r\n", lines)}\r\n";
}
