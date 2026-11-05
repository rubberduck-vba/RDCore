using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.3.1.11: what a member access given to a <c>ByRef</c> parameter is - the variable it names, which the callee writes, or a copy of its value.
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA printed for the same code in Excel (64-bit, en-US, oracle\s58 B01, W01): the callee adds 10 to a value of 1, so
/// 11 is the variable, and 1 a copy.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Procedure invocation argument processing")]
public sealed class MemberAccessByRefArgumentTests
{
    private static readonly string Box = ClassModule("Box",
        "Public V As Variant",
        "Public Sub SelfMe()",
        "    BumpV Me.V(0)",
        "End Sub",
        "Public Sub SelfBare()",
        "    BumpV V(0)",
        "End Sub");

    private static string Program(string body) => StandardModule("Program",
        "Private Type TPair",
        "    X As Long",
        "    Y As Long",
        "End Type",
        "Public Sub BumpV(ByRef x As Variant)",
        "    x = x + 10",
        "End Sub",
        "Public Sub BumpLong(ByRef n As Long)",
        "    n = n + 10",
        "End Sub",
        "Public Sub Main()",
        "    " + string.Join("\r\n    ", body.Split(" ;; ")),
        "End Sub");

    // an element of an array an object's public variable holds is a copy, through the object as through Me; in the class, the variable it is.
    [TestMethod]
    [DataRow("Dim b As New Box ;; b.V = Array(1, 2) ;; BumpV b.V(0) ;; Debug.Print b.V(0)", "1", DisplayName = "B01 b.V(0)")]
    [DataRow("Dim b As New Box ;; b.V = Array(1, 2) ;; With b ;; BumpV .V(0) ;; End With ;; Debug.Print b.V(0)", "1", DisplayName = "B01 .V(0) in With b")]
    [DataRow("Dim b As New Box ;; b.V = Array(1, 2) ;; b.SelfMe ;; Debug.Print b.V(0)", "1", DisplayName = "B01 Me.V(0)")]
    [DataRow("Dim b As New Box ;; b.V = Array(1, 2) ;; b.SelfBare ;; Debug.Print b.V(0)", "11", DisplayName = "B01 V(0) in the class")]
    [DataRow("Dim w As Variant ;; w = Array(1, 2) ;; BumpV w(0) ;; Debug.Print w(0)", "11", DisplayName = "B01 w(0) of a local Variant")]
    public async Task AnElementOfAnObjectsVariable_IsACopy_WhereTheClassesOwnIsTheVariable(string body, string expected)
        => await AssertPrintedInEitherEngineAsync(body, expected);

    // a field of a variable of a user-defined type is the variable it is, in a With block as without it.
    [TestMethod]
    [DataRow("Dim r As TPair ;; r.X = 1 ;; With r ;; BumpLong .X ;; End With ;; Debug.Print r.X", "11", DisplayName = "W01 .X in With r")]
    [DataRow("Dim r As TPair ;; r.X = 1 ;; BumpLong r.X ;; Debug.Print r.X", "11", DisplayName = "W01 r.X")]
    [DataRow("Dim t(1) As TPair ;; t(0).X = 1 ;; With t(0) ;; BumpLong .X ;; End With ;; Debug.Print t(0).X", "11", DisplayName = "W01 .X in With t(0)")]
    [DataRow("Dim t(1) As TPair ;; t(0).X = 1 ;; BumpLong t(0).X ;; Debug.Print t(0).X", "11", DisplayName = "W01 t(0).X")]
    public async Task AFieldOfAUserDefinedTypeVariable_IsTheVariable(string body, string expected)
        => await AssertPrintedInEitherEngineAsync(body, expected);

    private static async Task AssertPrintedInEitherEngineAsync(string body, string expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var observed = string.Join("|", await RunAsync([("Box", Box)], Program(body), engine));
            Assert.AreEqual(expected, observed, $"{engine}, which printed {observed}");
        }
    }
}
