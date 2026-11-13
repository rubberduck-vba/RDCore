using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Set</c> of what a call returns (<strong>MS-VBAL §5.4.3.9</strong>): a factory of a default instance, a function of a standard module, a
/// <c>Property Get</c> of an object; and a call that leaves out an <c>Optional</c> argument (<strong>MS-VBAL §5.3.1.11</strong>). Compiled code makes the
/// call: the interpreter does not run the statement for it. A program runs the same either way: which object each variable holds, when an object
/// terminates, the error a <c>Set</c> raises and where.
/// </summary>
[TestClass]
public sealed class CompiledObjectCallSetTests
{
    // a class with a default instance and a factory on it, whose objects say when they are made and when they terminate, and a chain of them.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Gadget", ModuleWorkspace.ClassModule("Gadget",
            "Attribute VB_PredeclaredId = True",
            "Option Explicit",
            "Private mSize As Long",
            "Private mNext As Gadget",
            "Public Function Create(ByVal Size As Long) As Gadget",
            "    Dim Result As Gadget",
            "    Set Result = New Gadget",
            "    Result.Size = Size",
            "    Set Create = Result",
            "End Function",
            "Public Property Get Size() As Long",
            "    Size = mSize",
            "End Property",
            "Public Property Let Size(ByVal Value As Long)",
            "    mSize = Value",
            "End Property",
            "Public Property Get Following() As Gadget",
            "    Set Following = mNext",
            "End Property",
            "Public Sub Link(ByVal Other As Gadget)",
            "    Set mNext = Other",
            "End Sub",
            "Public Function Nobody() As Gadget",
            "End Function",
            "Public Function Anything() As Object",
            "    Set Anything = New Collection",
            "End Function",
            "Public Sub Grow(Optional ByVal By As Long = 1)",
            "    mSize = mSize + By",
            "End Sub",
            "Public Function Grown(Optional ByVal By As Long = 10, Optional ByVal Times As Long = 2) As Long",
            "    Grown = mSize + By * Times",
            "End Function",
            "Public Function Hop() As Long",
            "    Dim G As Gadget",
            "    Set G = Me.Following",
            "    Hop = G.Size",
            "End Function",
            "Private Sub Class_Terminate()",
            "    If mSize > 0 Then Debug.Print \"gone\"; mSize",
            "End Sub")),
    ];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the program run both ways, printing expected; then the calls the procedure named made itself, through a typed entry or the invoker: none of them a
    // statement the interpreter ran for it. The first call of a member of the default instance is the interpreter's, which makes the object (MS-VBAL
    // §5.2.3.1.4): it is not counted. Under the random walk-back any activation may be handed to the interpreter, which makes the calls after.
    private static async Task AssertCalledAsync(string program, string caller, long calls, string expected)
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

    // a factory of the default instance, in a loop: each turn the variable holds the new object, and the one it held terminates when it is replaced.
    [TestMethod]
    public async Task ASetOfWhatAFactoryOfTheDefaultInstanceReturns_IsCompiled()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Made(3)",
            "    Debug.Print \"after\"",
            "End Sub",
            "Private Function Made(ByVal N As Long) As Long",
            "    Dim G As Gadget, I As Long",
            "    For I = 1 To N",
            "        Set G = Gadget.Create(I)",
            "        Made = Made + G.Size",
            "    Next",
            "End Function"), "Made", calls: 5, expected: "gone 1 ; gone 2 ; gone 3 ; 6 ; after");

    // a function of the standard module that returns an object, and a Set of the function's own result to what a call returns.
    [TestMethod]
    public async Task ASetOfWhatAFunctionReturns_AndOfTheFunctionsOwnResult_IsCompiled()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Sum(2)",
            "End Sub",
            "Private Function Make(ByVal Size As Long) As Gadget",
            "    Set Make = Gadget.Create(Size * 10)",
            "End Function",
            "Private Function Sum(ByVal N As Long) As Long",
            "    Dim G As Gadget, I As Long",
            "    For I = 1 To N",
            "        Set G = Make(I)",
            "        Sum = Sum + G.Size",
            "    Next",
            "End Function"), "Sum", calls: 4, expected: "gone 10 ; gone 20 ; 30");

    // the variable given what a Property Get of its own object returns: the object it held is let go of only once the call returned, and the next one is
    // what it holds after. A Property Get that returns Nothing leaves the variable holding Nothing.
    [TestMethod]
    public async Task ASetOfWhatAPropertyOfTheVariablesOwnObjectReturns_LetsGoOfTheOldOneAfter()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Walked()",
            "    Debug.Print \"after\"",
            "End Sub",
            "Private Function Walked() As Long",
            "    Dim G As Gadget, H As Gadget",
            "    Set G = Gadget.Create(1)",
            "    Set H = Gadget.Create(2)",
            "    G.Link H",
            "    Set H = Nothing",
            "    Set G = G.Following",
            "    Walked = G.Size",
            "    Set G = G.Following",
            "    Walked = Walked * 10",
            "    If G Is Nothing Then Walked = Walked + 1",
            "End Function"), "Walked", calls: 5, expected: "gone 1 ; gone 2 ; 21 ; after");

    // a member of the class whose code runs, called on Me for the object it returns.
    [TestMethod]
    public async Task ASetOfWhatAMemberOfMeReturns_IsCompiled()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Dim A As Gadget, B As Gadget",
            "    Set A = Gadget.Create(1)",
            "    Set B = Gadget.Create(7)",
            "    A.Link B",
            "    Set B = Nothing",
            "    Debug.Print A.Hop()",
            "    Set A = Nothing",
            "End Sub"), "Hop", calls: 2, expected: "7 ; gone 1 ; gone 7");

    // a function that returns no object: Nothing, which the variable holds; an Object of a class the variable is not: error 13, raised by the Set where it is.
    [TestMethod]
    public async Task ASetOfNothingReturned_HoldsNothing_AndOfAnotherClass_RaisesTypeMismatchAtTheSet()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Checked()",
            "End Sub",
            "Private Function Checked() As Long",
            "    Dim G As Gadget",
            "    Set G = Gadget.Create(5)",
            "    Set G = Gadget.Nobody()",
            "    If G Is Nothing Then Checked = 1",
            "    Set G = Gadget.Anything()",
            "    Checked = 2",
            "End Function"), "Checked", calls: 2, expected: "gone 5 | 13 VBR00013");

    // a member called with an Optional argument left out, as a statement and in an expression: the parameter is given its default, as the interpreter gives
    // it; one given is given what it is given.
    [TestMethod]
    public async Task AMemberCall_LeavingOutAnOptionalArgument_IsCompiled()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Grew(3)",
            "End Sub",
            "Private Function Grew(ByVal N As Long) As Long",
            "    Dim G As Gadget, I As Long",
            "    Set G = New Gadget",
            "    For I = 1 To N",
            "        G.Grow",
            "        G.Grow 2",
            "    Next",
            "    Grew = G.Grown + G.Grown(1) + G.Grown(, 3)",
            "    Set G = Nothing",
            "End Function"), "Grew", calls: 9, expected: "gone 9 ; 79");
}
