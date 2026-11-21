using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Set</c> of an object variable declared <c>As New</c> (MS-VBAL §5.4.3.9): compiled code assigns it what it is given, <c>Nothing</c> or an object.
/// The <c>Set</c> does not read the variable, so it makes no object: the next read makes one when the variable holds <c>Nothing</c>
/// (MS-VBAL §5.2.3.1.4). A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledAsNewSetTests
{
    // a class whose Initialize prints, that adds up the numbers it is given.
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
            "End Property")),
    ];

    // the program run both ways, and each Set of the variable named target, in the procedure compiled, assigned by compiled code: no reason, stepped
    // statement or support call is on its line, nor any that has no line. What else the procedure does is not these tests' subject.
    private static async Task AssertSetsCompiledAsync(string procedure, string target, string expected, params string[] lines)
    {
        var program = Program(lines);
        var sets = program.Split("\r\n").Select((line, number) => (line, number))
            .Where(each => each.line.TrimStart().StartsWith($"Set {target} = ", StringComparison.Ordinal)).Select(each => each.number).ToArray();
        Assert.IsNotEmpty(sets, $"the program has no Set of {target}: the row tests nothing");
        var compiled = await AssertAlikeAsync(program, Classes, expected);
        Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
        Assert.IsNotNull(result.Body,
            $"{procedure} is no longer compiled, so its Sets are not tested: "
                + string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
        var refused = result.Reasons.Concat(result.Stepped).Concat(result.SupportCalls)
            .Where(reason => reason.Location is not { } at || sets.Contains(at.Range.Start.Line))
            .Select(reason => $"{reason.Kind} {reason.Detail} {reason.Location}")
            .ToArray();
        Assert.IsEmpty(refused, $"{procedure}'s Set of {target}: {string.Join("; ", refused)}");
    }

    // a Set to Nothing of a local As New: the next call makes another object.
    [TestMethod]
    public async Task ASetToNothingOfALocalAsNew_IsAssignedByCompiledCode()
        => await AssertSetsCompiledAsync("Again", "M", "mademade 2 ",
            "Public Sub Main()",
            "    Debug.Print Again()",
            "End Sub",
            "Private Function Again() As Long",
            "    Dim M As New Marker",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        If M.Marked(3) Then Again = Again + 1",
            "        Set M = Nothing",
            "    Next",
            "End Function");

    // a Set of a local As New to an object: the variable holds that object, and no other is made.
    [TestMethod]
    public async Task ASetOfALocalAsNewToAnObject_IsAssignedByCompiledCode()
        => await AssertSetsCompiledAsync("Given", "M", "made 6 ",
            "Public Sub Main()",
            "    Dim O As Marker",
            "    Set O = New Marker",
            "    Debug.Print Given(O)",
            "End Sub",
            "Private Function Given(ByVal O As Marker) As Long",
            "    Dim M As New Marker",
            "    Set M = O",
            "    If M.Marked(5) Then Given = 1",
            "    Given = Given + O.Count",
            "End Function");

    // a Set to Nothing of a variable of the module declared As New: the next activation's read makes another object.
    [TestMethod]
    public async Task ASetToNothingOfAModuleVariableAsNew_IsAssignedByCompiledCode()
        => await AssertSetsCompiledAsync("Counted", "Kept", "mademade 2 ",
            "Private Kept As New Marker",
            "Public Sub Main()",
            "    Dim N As Long",
            "    N = Counted()",
            "    N = N + Counted()",
            "    Debug.Print N",
            "End Sub",
            "Private Function Counted() As Long",
            "    If Kept.Marked(1) Then Counted = 10",
            "    Counted = Counted + Kept.Count",
            "    Set Kept = Nothing",
            "End Function");
}
