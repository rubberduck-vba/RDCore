using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Let</c> of a public variable of an object (<strong>MS-VBAL §5.4.3.8</strong>), of an object variable or of the object of a <c>With</c> block declared
/// as a class: compiled code writes the value, let-coerced to the variable's type, in the variable's cell, with no support call. The variable is the one the
/// object's class has, found each time: an object of a class that implements the declared one as an interface has a <c>Property Let</c> for it, which the
/// interpreter calls. An object variable that holds <c>Nothing</c>, a variable that is no scalar (a <c>Variant</c>, which may hold an object), and a value
/// out of the variable's range are the interpreter's. A program runs as it does in the interpreter.
/// </summary>
[TestClass]
public sealed class CompiledMemberFieldLetTests
{
    // a class whose public variables are written; a class used as an interface, and one that implements it; an object that says when it terminates.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Point", ModuleWorkspace.ClassModule("Point",
            "Option Explicit",
            "Public X As Long",
            "Public R As Double",
            "Public B As Byte",
            "Public Flag As Boolean",
            "Public V As Variant",
            "Public Property Let Bump(ByRef Value As Long)",
            "    X = X + Value",
            "    Value = 99",
            "End Property")),
        ("IShape", ModuleWorkspace.ClassModule("IShape", "Option Explicit", "Public Size As Long")),
        ("Square", ModuleWorkspace.ClassModule("Square",
            "Option Explicit",
            "Implements IShape",
            "Private mSize As Long",
            "Private Property Let IShape_Size(ByVal Value As Long)",
            "    Debug.Print \"square\"; Value",
            "    mSize = Value",
            "End Property",
            "Private Property Get IShape_Size() As Long",
            "    IShape_Size = mSize",
            "End Property")),
        ("Noisy", ModuleWorkspace.ClassModule("Noisy",
            "Option Explicit",
            "Private Sub Class_Terminate()",
            "    Debug.Print \"gone\"",
            "End Sub")),
    ];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the program run both ways, printing expected; then the statements the procedure named had the interpreter run as support calls. Under the random
    // walk-back any activation may be handed to the interpreter, which runs the statements after.
    private static async Task AssertLetAsync(string program, string procedure, long supportCalls, string expected)
    {
        var compiled = await AssertAlikeAsync(program, Classes, expected);
        Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
        var body = result.Body ?? throw new AssertFailedException($"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        if (CompiledExecution.HandsOverAtRandom)
        {
            return;
        }

        Assert.AreEqual(supportCalls, body.SupportCallsMade, $"{procedure}'s support calls");
    }

    // a Long, a Double and a Boolean public variable of an object a parameter refers to, each written in a loop and read after it, where the object holds them.
    [TestMethod]
    public async Task APublicVariableOfAnObject_IsWrittenByCompiledCode()
        => await AssertLetAsync(Program(
            "Public Sub Main()",
            "    Dim P As Point",
            "    Set P = New Point",
            "    Debug.Print Filled(P, 10)",
            "    Debug.Print P.X; P.R",
            "End Sub",
            "Private Function Filled(ByVal P As Point, ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        P.X = I",
            "        P.R = I / 4",
            "        P.Flag = I Mod 2 = 0",
            "    Next",
            "    Filled = P.X * 1000 + P.R * 10 - P.Flag",
            "End Function"), "Filled", supportCalls: 0, expected: "10026");

    // the value let-coerced to the variable's type: a Double to a Long, rounded to even; then a value out of a Byte's range, error 6 at the statement.
    [TestMethod]
    public async Task TheValue_IsLetCoercedToTheVariablesType_AndOneOutOfItsRange_IsAnOverflowAtTheStatement()
        => await AssertLetAsync(Program(
            "Public Sub Main()",
            "    Dim P As Point",
            "    Set P = New Point",
            "    Coerced P",
            "End Sub",
            "Private Sub Coerced(ByVal P As Point)",
            "    Dim I As Long, Total As Long",
            "    For I = 1 To 10",
            "        P.X = I / 4",
            "        Total = Total + P.X",
            "    Next",
            "    Debug.Print Total",
            "    P.B = 255",
            "    Debug.Print P.B",
            "    P.B = 245 + I",
            "    Debug.Print \"not here\"",
            "End Sub"), "Coerced", supportCalls: 0, expected: "13 ; 255 | 6 VBR00006");

    // a public variable of the object of a With block, written in a loop inside the block.
    [TestMethod]
    public async Task APublicVariableOfTheObjectOfAWithBlock_IsWrittenByCompiledCode()
        => await AssertLetAsync(Program(
            "Public Sub Main()",
            "    Dim P As Point",
            "    Set P = New Point",
            "    Debug.Print Within(P, 5)",
            "End Sub",
            "Private Function Within(ByVal P As Point, ByVal N As Long) As Long",
            "    Dim I As Long",
            "    With P",
            "        For I = 1 To N",
            "            .X = .X + I",
            "        Next",
            "    End With",
            "    Within = P.X",
            "End Function"), "Within", supportCalls: 0, expected: "15");

    // an object of a class that implements the declared class as an interface: the Property Let of its own class is called, by the interpreter, each time
    // the statement runs; an object of the declared class has its variable written.
    [TestMethod]
    public async Task AnObjectOfAClassThatImplementsTheDeclaredOne_HasItsPropertyLetCalled()
        => await AssertLetAsync(Program(
            "Public Sub Main()",
            "    Dim S As IShape, Plain As IShape",
            "    Set S = New Square",
            "    Set Plain = New IShape",
            "    Debug.Print Sized(S, Plain)",
            "End Sub",
            "Private Function Sized(ByVal S As IShape, ByVal Plain As IShape) As Long",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        S.Size = I",
            "        Plain.Size = I * 10",
            "    Next",
            "    Sized = S.Size + Plain.Size",
            "End Function"), "Sized", supportCalls: 0, expected: "square 1 ; square 2 ; 22");

    // an object variable declared As New holds no object until its member is used: the interpreter makes it, then writes the variable; one that holds Nothing
    // is error 91, at the statement.
    [TestMethod]
    public async Task AnObjectVariableWithNoObject_IsTheInterpreters_WhichMakesItOrRaisesError91()
        => await AssertLetAsync(Program(
            "Public Sub Main()",
            "    Unset",
            "End Sub",
            "Private Sub Unset()",
            "    Dim Q As New Point, P As Point",
            "    Q.X = 5",
            "    Debug.Print Q.X",
            "    P.X = 1",
            "    Debug.Print \"not here\"",
            "End Sub"), "Unset", supportCalls: 0, expected: "5 | 91 VBR00091");

    // a Variant public variable holding an object, given a number: the object it held is let go of, and terminates, as the interpreter writes the cell.
    [TestMethod]
    public async Task AVariantPublicVariableHoldingAnObject_IsWrittenByTheInterpreter_AndTheObjectTerminates()
        => await AssertLetAsync(Program(
            "Public Sub Main()",
            "    Dim P As Point",
            "    Set P = New Point",
            "    Replaced P",
            "    Debug.Print P.V",
            "End Sub",
            "Private Sub Replaced(ByVal P As Point)",
            "    Set P.V = New Noisy",
            "    P.V = 5",
            "    Debug.Print \"after\"",
            "End Sub"), "Replaced", supportCalls: 2, expected: "gone ; after ; 5");

    // a Property Let whose value parameter is ByRef is given the value, not the variable: what it assigns to its parameter leaves the variable as it was.
    [TestMethod]
    public async Task APropertyLetsByRefValueParameter_LeavesTheVariableGivenAsItWas()
        => await AssertLetAsync(Program(
            "Public Sub Main()",
            "    Dim P As Point",
            "    Set P = New Point",
            "    Debug.Print Bumped(P)",
            "End Sub",
            "Private Function Bumped(ByVal P As Point) As Long",
            "    Dim I As Long",
            "    I = 3",
            "    P.Bump = I",
            "    Bumped = I * 100 + P.X",
            "End Function"), "Bumped", supportCalls: 0, expected: "303");

    // the value is evaluated before the object (MS-VBAL §5.4.3.8): a function that Sets the variable to another object, called for the value, has the value
    // written to that other object.
    [TestMethod]
    public async Task TheValueIsEvaluatedBeforeTheObject_WhichAFunctionCalledForIt_MayChange()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim P As Point, First As Point",
            "    Set P = New Point",
            "    Set First = P",
            "    P.X = Swap(P)",
            "    Debug.Print First.X; P.X",
            "End Sub",
            "Private Function Swap(ByRef Q As Point) As Long",
            "    Set Q = New Point",
            "    Swap = 7",
            "End Function"), Classes, "0  7");
}
