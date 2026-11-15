using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A call that leaves out an <c>Optional</c> argument (<strong>MS-VBAL §5.3.1.11</strong>), made by compiled code: a <c>Variant</c> parameter is given
/// <c>Missing</c> or the default it declares, and a typed one its default, as the interpreter gives them. A call of a member of an object, of a member
/// through an interface it implements, and of a procedure of a standard module, with parentheses and without.
/// </summary>
[TestClass]
public sealed class CompiledOmittedArgumentTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Tally", ModuleWorkspace.ClassModule("Tally",
            "Option Explicit",
            "Private mCount As Long",
            "Public Function Kind(Optional ByVal V As Variant) As Long",
            "    If IsMissing(V) Then Kind = -1 Else Kind = VarType(V)",
            "End Function",
            "Public Function Scaled(Optional ByVal By As Variant = 5, Optional ByRef Into As Variant) As Long",
            "    Scaled = By * 2 + VarType(By)",
            "    If IsMissing(Into) Then Scaled = Scaled + 100",
            "End Function",
            "Public Sub Touch(Optional ByVal V As Variant)",
            "    If IsMissing(V) Then mCount = mCount + 1",
            "End Sub",
            "Public Property Get Count() As Long",
            "    Count = mCount",
            "End Property")),
        ("IShape", ModuleWorkspace.ClassModule("IShape",
            "Option Explicit",
            "Public Function Area(Optional ByVal Factor As Long = 3, Optional ByVal Unit As Variant) As Long",
            "End Function")),
        ("Square", ModuleWorkspace.ClassModule("Square",
            "Option Explicit",
            "Implements IShape",
            "Private Function IShape_Area(Optional ByVal Factor As Long = 3, Optional ByVal Unit As Variant) As Long",
            "    IShape_Area = Factor * 10 - IsMissing(Unit)",
            "End Function")),
    ];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the program run both ways, printing expected; then the calls the procedure named made itself, through a typed entry or the invoker: none of them a
    // statement the interpreter ran for it. Under the random walk-back any activation may be handed to the interpreter, which makes the calls after.
    private static async Task AssertCalledAsync(string program, string caller, long calls, string? expected)
    {
        var compiled = await AssertAlikeAsync(program, Classes, expected);
        Assert.IsTrue(compiled.TryGetValue(caller, out var result), $"{caller} was not called");
        var body = result.Body ?? throw new AssertFailedException($"{caller}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        if (CompiledExecution.HandsOverAtRandom)
        {
            return;
        }

        Assert.AreEqual(calls, body.DirectCalls + body.IndirectCalls, $"{caller}'s calls made by compiled code");
    }

    // a member of an object called with its Optional Variants left out, as a statement and in an expression: Missing, a declared default of its own subtype
    // (Integer), and Missing given to a parameter passed by reference, which the callee holds in a cell of its own.
    [TestMethod]
    public async Task AMemberCall_LeavingOutAnOptionalVariant_IsCompiled()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Tallied(3)",
            "End Sub",
            "Private Function Tallied(ByVal N As Long) As Long",
            "    Dim T As Tally, I As Long, R As Long",
            "    Set T = New Tally",
            "    For I = 1 To N",
            "        T.Touch",
            "        R = R + T.Kind + T.Scaled",
            "    Next",
            "    Tallied = R * 10 + T.Count",
            "End Function"), "Tallied", calls: 10, expected: " 3333");

    // a function of a standard module called with its Optional arguments left out, with empty parentheses and named by itself: a Variant given Missing, or
    // the String it declares as its default, passed by reference; a Long given its default.
    [TestMethod]
    public async Task AProcedureCall_LeavingOutItsOptionalArguments_IsCompiled()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed(2)",
            "End Sub",
            "Private Function Summed(ByVal N As Long) As Long",
            "    Dim I As Long, R As Long",
            "    For I = 1 To N",
            "        R = R + Kind() + Kind + Twice + Twice() + Mark",
            "    Next",
            "    Summed = R",
            "End Function",
            "Private Function Kind(Optional ByVal V As Variant) As Long",
            "    If IsMissing(V) Then Kind = -1 Else Kind = VarType(V)",
            "End Function",
            "Private Function Twice(Optional ByVal N As Long = 4) As Long",
            "    Twice = N * 2",
            "End Function",
            "Private Function Mark(Optional ByRef V As Variant = \"x\") As Long",
            "    If VarType(V) = vbString Then Mark = 1",
            "End Function"), "Summed", calls: 10, expected: " 30");

    // a member called through the interface the object's class implements, its Optional arguments left out: each is given the default of the member
    // invoked, which is the one of the interface member (MS-VBAL §5.3.1.9).
    [TestMethod]
    public async Task AnInterfaceMemberCall_LeavingOutOptionalArguments_IsCompiled()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Measured(2)",
            "End Sub",
            "Private Function Measured(ByVal N As Long) As Long",
            "    Dim S As IShape, I As Long, R As Long",
            "    Set S = New Square",
            "    For I = 1 To N",
            "        R = R + S.Area + S.Area(1)",
            "    Next",
            "    Measured = R",
            "End Function"), "Measured", calls: 4, expected: " 84");
}
