using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A call of a member of <c>Me</c>, compiled: written <c>Me.Member</c>, or by the member's bare name, which is a member of the current instance
/// (<strong>MS-VBAL §5.6.10</strong>), it is made on the activation's own object, as a statement and inside an expression, its result a scalar or a
/// <c>String</c>. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledMeCallTests
{
    private static readonly (string Name, string Source)[] Tallies =
    [
        ("Tally", ModuleWorkspace.ClassModule("Tally", "Option Explicit",
            "Private Total As Long",
            "Private Times As Long",
            "Public Sub Add(ByVal Amount As Long)",
            "    Total = Total + Twice(Amount)",
            "    Tick",
            "End Sub",
            "Public Sub Tick()",
            "    Times = Times + 1",
            "End Sub",
            "Public Function Twice(ByVal N As Long) As Long",
            "    Twice = N * 2",
            "End Function",
            "Public Property Get Sum() As Long",
            "    Sum = Total",
            "End Property",
            "Public Property Get Ticks() As Long",
            "    Ticks = Times",
            "End Property",
            "Public Property Get Cell(ByVal I As Long) As Long",
            "    Cell = I * I",
            "End Property",
            "Public Sub Run(ByVal N As Long)",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Me.Add I",
            "        Add I",
            "        Me.Tick",
            "        Tick",
            "        Me.Twice I",
            "        Call Twice(I)",
            "    Next",
            "End Sub",
            "Public Function Scaled(ByVal N As Long) As Long",
            "    Dim A As Long",
            "    Dim B As Long",
            "    A = Me.Twice(N)",
            "    B = Twice(A)",
            "    Scaled = Twice(B)",
            "End Function",
            "Public Function Mixed(ByVal N As Long) As Long",
            "    Mixed = Me.Twice(N) + Twice(N + 1) * Sum - Ticks",
            "    If Twice(N) > Sum Then",
            "        Mixed = Mixed + 1",
            "    End If",
            "    Do While Twice(N) < Me.Sum",
            "        N = N * 3",
            "    Loop",
            "    Mixed = Mixed * 1000 + N",
            "End Function",
            "Public Function Cells(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Cells = Cells + Cell(I) + Me.Cell(I)",
            "    Next",
            "End Function",
            "Public Function Describe() As String",
            "    Describe = \"[\" & Total & \"]\"",
            "End Function",
            "Public Function Label() As String",
            "    Label = Describe",
            "End Function",
            "Public Function Labels() As String",
            "    Dim S As String",
            "    S = Me.Describe",
            "    Labels = S & \"/\" & S",
            "End Function",
            "Public Function Safe(ByVal N As Long) As Long",
            "    On Error Resume Next",
            "    Safe = -1",
            "    Safe = Divide(N)",
            "    Safe = Safe * 10",
            "End Function",
            "Private Function Divide(ByVal N As Long) As Long",
            "    Divide = 100 \\ N",
            "End Function",
            "Public Function Fact(ByVal N As Long) As Long",
            "    If N <= 1 Then",
            "        Fact = 1",
            "    Else",
            "        Fact = N * Fact(N - 1)",
            "    End If",
            "End Function",
            "Public Function Compare(ByVal Other As Tally) As Long",
            "    Absorb Other",
            "    Compare = Difference(Other)",
            "End Function",
            "Private Sub Absorb(ByVal Other As Tally)",
            "    Total = Total + Other.Sum",
            "End Sub",
            "Private Function Difference(ByVal Other As Tally) As Long",
            "    Difference = Sum - Other.Sum",
            "End Function")),
    ];

    // each procedure named was compiled, none of its statements was the interpreter's, and none of its instructions was handed to it after a call.
    private static void AssertCompiled(IReadOnlyDictionary<string, CompilationResult> compiled, params string[] names)
    {
        foreach (var name in names)
        {
            Assert.IsTrue(compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            Assert.IsEmpty(result.Stepped, $"{name} steps: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(0, result.Body.WalkBacks, $"{name}'s walk-backs");
            }
        }
    }

    // a Sub of Me, and a Function whose result is let go of, called as statements: written on Me, by their bare names, and with Call.
    [TestMethod]
    public async Task MembersOfMeCalledAsStatements_RunOnTheObject()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim T As New Tally",
            "    T.Run 3",
            "    Debug.Print T.Sum; T.Ticks",
            "End Sub"), classes: Tallies, expected: " 24  12"), "Run", "Add", "Tick");

    // a Function of Me whose result a variable is assigned, and the function's own result.
    [TestMethod]
    public async Task AFunctionOfMeAssigned_IsWhatItReturns()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim T As New Tally",
            "    Debug.Print T.Scaled(3)",
            "End Sub"), classes: Tallies, expected: " 24"), "Scaled", "Twice");

    // members of Me inside expressions: operands of an operation, a condition of an If, and a condition of a loop, a Property Get read by its bare name
    // among them.
    [TestMethod]
    public async Task MembersOfMeInsideExpressions_AreWhatTheyReturn()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim T As New Tally",
            "    T.Add 5",
            "    Debug.Print T.Mixed(2)",
            "End Sub"), classes: Tallies, expected: " 63006"), "Mixed", "Sum", "Ticks");

    // a Property Get of Me given an argument, by its bare name and on Me, inside an expression in a loop.
    [TestMethod]
    public async Task APropertyGetOfMeGivenAnArgument_IsWhatItReturns()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim T As New Tally",
            "    Debug.Print T.Cells(4)",
            "End Sub"), classes: Tallies, expected: " 60"), "Cells", "Cell");

    // a String function of Me assigned to the result of a String function, by its bare name; and to a String variable, on Me.
    [TestMethod]
    public async Task AStringFunctionOfMeAssigned_IsItsText()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim T As New Tally",
            "    T.Add 21",
            "    Debug.Print T.Label; \"|\"; T.Labels",
            "End Sub"), classes: Tallies, expected: "[42]|[42]/[42]"), "Label", "Labels", "Describe");

    // an error a member of Me raises is the caller's to handle: the statement that called it is resumed after.
    [TestMethod]
    public async Task AnErrorAMemberOfMeRaises_IsHandledByTheCaller()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim T As New Tally",
            "    Debug.Print T.Safe(0); T.Safe(5)",
            "End Sub"), classes: Tallies, expected: "-10  200"), "Safe");

    // a member of Me that calls itself by its bare name.
    [TestMethod]
    public async Task AMemberOfMeCallingItself_Recurses()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim T As New Tally",
            "    Debug.Print T.Fact(10)",
            "End Sub"), classes: Tallies, expected: " 3628800"), "Fact");

    // members of Me given an object: another one, and Me itself.
    [TestMethod]
    public async Task MembersOfMeGivenAnObject_AreGivenIt()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As New Tally",
            "    Dim B As New Tally",
            "    A.Add 2",
            "    B.Add 3",
            "    Debug.Print A.Compare(B); A.Compare(A)",
            "End Sub"), classes: Tallies, expected: " 4  0"), "Compare", "Absorb", "Difference");
}
