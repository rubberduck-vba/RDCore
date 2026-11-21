using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A member of a class of the workspace, called on an object variable declared <c>As New</c>: compiled code calls it on the object the variable holds.
/// While the variable holds <c>Nothing</c>, the interpreter makes the call, and with it the object the variable is given (MS-VBAL §5.2.3.1.4), as it does
/// after a <c>Set</c> to <c>Nothing</c>. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledAsNewMemberCallTests
{
    // a class whose Initialize prints, that adds up the numbers it is given, and whose default member multiplies the sum.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Marker", ClassModule("Marker",
            "Option Explicit",
            "Private m As Long",
            "Private Sub Class_Initialize()",
            "    Debug.Print \"made\";",
            "End Sub",
            "Public Function Marked(ByVal N As Long) As Boolean",
            "    m = m + N",
            "    Marked = m > 2",
            "End Function",
            "Public Property Get Count() As Long",
            "    Count = m",
            "End Property",
            "Public Property Get Item(ByVal K As Long) As Long",
            "Attribute Item.VB_UserMemId = 0",
            "    Item = m * K",
            "End Property")),
    ];

    private static string Reasons(CompilationResult result) => string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"));

    // a local As New: the first call makes the object, which the next ones are made on.
    [TestMethod]
    public async Task AMemberOfALocalAsNew_IsCalledByCompiledCode()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Twice()",
            "End Sub",
            "Private Function Twice() As Long",
            "    Dim M As New Marker",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        If M.Marked(2) Then Twice = Twice + M.Count",
            "    Next",
            "End Function"), Classes, expected: "made 10 ");
        Assert.IsTrue(compiled.TryGetValue("Twice", out var result), "Twice was not called");
        Assert.IsNotNull(result.Body, string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
        Assert.IsEmpty(result.Stepped, Reasons(result));
    }

    // the default member of a local As New, called by an index expression (MS-VBAL §5.6.13): the first call makes the object too.
    [TestMethod]
    public async Task TheDefaultMemberOfALocalAsNew_IsCalledByCompiledCode()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Doubled()",
            "End Sub",
            "Private Function Doubled() As Long",
            "    Dim M As New Marker",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        Doubled = Doubled + M(I)",
            "        If M.Marked(1) Then Doubled = Doubled + 100",
            "    Next",
            "End Function"), Classes, expected: "made 2 ");
        Assert.IsTrue(compiled.TryGetValue("Doubled", out var result), "Doubled was not called");
        Assert.IsNotNull(result.Body, string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
        Assert.IsEmpty(result.Stepped, Reasons(result));
    }

    // a Set to Nothing leaves the variable holding none: the next call makes another object.
    [TestMethod]
    public async Task AMemberOfALocalAsNewSetToNothing_MakesAnotherObject()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Again()",
            "End Sub",
            "Private Function Again() As Long",
            "    Dim M As New Marker",
            "    If M.Marked(3) Then Again = 1",
            "    Set M = Nothing",
            "    If M.Marked(3) Then Again = Again + 10",
            "End Function"), Classes, expected: "mademade 11 ");

    // a variable of the module declared As New, made by the first call of the first activation.
    [TestMethod]
    public async Task AMemberOfAModuleVariableAsNew_IsCalledByCompiledCode()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Private Kept As New Marker",
            "Public Sub Main()",
            "    Debug.Print Counted(); Counted()",
            "End Sub",
            "Private Function Counted() As Long",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        If Kept.Marked(1) Then Counted = Counted + 1",
            "    Next",
            "End Function"), Classes, expected: "made 1  3 ");
        Assert.IsTrue(compiled.TryGetValue("Counted", out var result), "Counted was not called");
        Assert.IsNotNull(result.Body, string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
        Assert.IsEmpty(result.Stepped, Reasons(result));
    }
}
