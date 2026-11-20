using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>With</c> block of an object variable declared <c>As New</c> (MS-VBAL §5.4.2.21): compiled code opens it on the object the variable holds. While
/// the variable holds <c>Nothing</c>, the interpreter reads it, which makes the object the variable is given (MS-VBAL §5.2.3.1.4), and the block holds
/// that object. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledAsNewWithTests
{
    // a class whose Initialize prints, with a public variable and a member that adds up the numbers it is given.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Marker", ClassModule("Marker",
            "Option Explicit",
            "Public Total As Long",
            "Private m As Long",
            "Private Sub Class_Initialize()",
            "    Debug.Print \"made\";",
            "End Sub",
            "Public Function Marked(ByVal N As Long) As Boolean",
            "    m = m + N",
            "    Marked = m > 2",
            "End Function")),
    ];

    // the program run both ways: the procedure compiled, with no statement stepped, nor made a support call unless supported is.
    private static async Task AssertNoneSteppedAsync(string procedure, string expected, params string[] lines)
        => await AssertNoneSteppedAsync(procedure, expected, supported: false, lines);

    private static async Task AssertNoneSteppedAsync(string procedure, string expected, bool supported, params string[] lines)
    {
        var compiled = await AssertAlikeAsync(Program(lines), Classes, expected);
        Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
        Assert.IsNotNull(result.Body, $"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        Assert.IsEmpty(result.Stepped, $"{procedure} steps: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        Assert.IsTrue(supported || result.SupportCalls.Count == 0,
            $"{procedure} support calls: {string.Join("; ", result.SupportCalls.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
    }

    // a local As New: the first With makes the object, which the next ones hold.
    [TestMethod]
    public async Task AWithOfALocalAsNew_IsOpenedByCompiledCode()
        => await AssertNoneSteppedAsync("Summed", "made 10 ",
            "Public Sub Main()",
            "    Debug.Print Summed()",
            "End Sub",
            "Private Function Summed() As Long",
            "    Dim M As New Marker",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        With M",
            "            .Total = .Total + I",
            "            Summed = Summed + .Total",
            "        End With",
            "    Next",
            "End Function");

    // a member of the object of the block called, the block opened before the variable is read anywhere else.
    [TestMethod]
    public async Task AMemberOfTheObjectOfAWithOfALocalAsNew_IsCalledByCompiledCode()
        => await AssertNoneSteppedAsync("Counted", "made 2 ",
            "Public Sub Main()",
            "    Debug.Print Counted()",
            "End Sub",
            "Private Function Counted() As Long",
            "    Dim M As New Marker",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        With M",
            "            If .Marked(1) Then Counted = Counted + 1",
            "        End With",
            "    Next",
            "    Counted = Counted + 1",
            "End Function");

    // a Set to Nothing leaves the variable holding none: the next With makes another object, and the block opened before holds the first. The Set is not
    // this test's subject, which compiled code or the interpreter may run: the With statements are, and neither is stepped.
    [TestMethod]
    public async Task AWithOfALocalAsNewSetToNothing_MakesAnotherObject()
        => await AssertNoneSteppedAsync("Again", "mademade 15 ", supported: true,
            "Public Sub Main()",
            "    Debug.Print Again()",
            "End Sub",
            "Private Function Again() As Long",
            "    Dim M As New Marker",
            "    With M",
            "        .Total = 1",
            "        Set M = Nothing",
            "        Again = .Total * 10",
            "    End With",
            "    With M",
            "        .Total = .Total + 5",
            "        Again = Again + .Total",
            "    End With",
            "End Function");

    // a variable of the module declared As New, made by the first With of the first activation.
    [TestMethod]
    public async Task AWithOfAModuleVariableAsNew_IsOpenedByCompiledCode()
        => await AssertNoneSteppedAsync("Kept", "made 1  2 ",
            "Private Held As New Marker",
            "Public Sub Main()",
            "    Debug.Print Kept(); Kept()",
            "End Sub",
            "Private Function Kept() As Long",
            "    With Held",
            "        .Total = .Total + 1",
            "        Kept = .Total",
            "    End With",
            "End Function");
}
