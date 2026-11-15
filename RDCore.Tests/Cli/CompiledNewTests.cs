using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Set</c> given a <c>New</c> (<strong>MS-VBAL §5.4.3.9</strong>, <strong>§5.6.8</strong>), of an object variable or of the function result variable:
/// compiled code makes the object as the interpreter makes it, with no support call, and the object runs its <c>Initialize</c> before the <c>Set</c> is
/// given it (<strong>MS-VBAL §5.3.1.10</strong>). The object it replaces terminates once the new one is assigned; an error <c>Initialize</c> leaves
/// unhandled is the <c>Set</c>'s, which assigns nothing. A program runs as it does in the interpreter.
/// </summary>
[TestClass]
public sealed class CompiledNewTests
{
    // a class with no Initialize and no Terminate; one that says when it is made and when it terminates; one whose Initialize raises an error.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Plain", ModuleWorkspace.ClassModule("Plain",
            "Option Explicit",
            "Public N As Long")),
        ("Greeter", ModuleWorkspace.ClassModule("Greeter",
            "Option Explicit",
            "Public N As Long",
            "Private Sub Class_Initialize()",
            "    Debug.Print \"hello\"",
            "End Sub",
            "Private Sub Class_Terminate()",
            "    Debug.Print \"bye\"",
            "End Sub")),
        ("Faulty", ModuleWorkspace.ClassModule("Faulty",
            "Option Explicit",
            "Private Sub Class_Initialize()",
            "    Err.Raise 5",
            "End Sub")),
    ];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the program run both ways, printing expected; then the statements the procedure named had the interpreter run as support calls. Under the random
    // walk-back any activation may be handed to the interpreter, which runs the statements after.
    private static async Task AssertMadeAsync(string program, string procedure, long supportCalls, string expected)
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

    // an object of a class with no Initialize, made in a loop: each turn the variable holds the new object, whose public variable is written and read.
    [TestMethod]
    public async Task ASetOfNew_OfAClassWithNoInitialize_IsCompiled()
        => await AssertMadeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Made(3)",
            "End Sub",
            "Private Function Made(ByVal N As Long) As Long",
            "    Dim P As Plain, I As Long, T As Long",
            "    For I = 1 To N",
            "        Set P = New Plain",
            "        P.N = P.N + I",
            "        T = T + P.N",
            "    Next",
            "    Made = T",
            "End Function"), "Made", supportCalls: 0, expected: "| 6 |");

    // an object whose Initialize says it is made, in a loop: it is made where the Set is, before the statement after it; the object it replaces terminates
    // once it is assigned, after the new one's Initialize.
    [TestMethod]
    public async Task ASetOfNew_RunsInitializeBeforeTheSet_AndTheReplacedObjectTerminatesAfter()
        => await AssertMadeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Made(2)",
            "End Sub",
            "Private Function Made(ByVal N As Long) As Long",
            "    Dim G As Greeter, I As Long",
            "    For I = 1 To N",
            "        Debug.Print I",
            "        Set G = New Greeter",
            "        G.N = I * 10",
            "        Debug.Print G.N",
            "    Next",
            "    Set G = Nothing",
            "    Made = N",
            "End Function"), "Made", supportCalls: 0, expected: "| 1 ; hello ; 10 ; 2 ; hello ; bye ; 20 ; bye ; 2 |");

    // a Set of the function result variable to a New: the caller is given the object, which terminates when nothing holds it.
    [TestMethod]
    public async Task ASetOfTheFunctionResultToNew_IsCompiled()
        => await AssertMadeAsync(Program(
            "Public Sub Main()",
            "    Dim G As Greeter",
            "    Set G = Fresh()",
            "    Debug.Print G.N",
            "End Sub",
            "Private Function Fresh() As Greeter",
            "    Set Fresh = New Greeter",
            "End Function"), "Fresh", supportCalls: 0, expected: "| hello ; 0 ; bye |");

    // an error Initialize leaves unhandled is the Set's: the activation's handler is given it, and the variable is assigned nothing.
    [TestMethod]
    public async Task AnErrorInInitialize_IsTheSets_AndAssignsNothing()
        => await AssertMadeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Tried()",
            "End Sub",
            "Private Function Tried() As Long",
            "    Dim F As Faulty",
            "    On Error Resume Next",
            "    Set F = New Faulty",
            "    Tried = Err.Number * 10 - (F Is Nothing)",
            "End Function"), "Tried", supportCalls: 0, expected: "| 51 |");

    // the same error with no handler: the program stops with it, raised in Initialize, the caller located at the Set (line 5), as the interpreter stops it.
    [TestMethod]
    public async Task AnErrorInInitialize_WithNoHandler_StopsTheProgramAtTheSet()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Tried()",
            "End Sub",
            "Private Function Tried() As Long",
            "    Dim F As Faulty",
            "    Set F = New Faulty",
            "    Tried = 1",
            "End Function"), Classes, expected: "| 5 VBA00005 Invalid procedure call or argument @7:4 | Class_Initialize@7:4 < Tried@5:4 < Main@1:16");

    // a New of a class of the standard library, made by the runtime as the interpreter makes it.
    [TestMethod]
    public async Task ASetOfNew_OfALibraryClass_RunsAsInTheInterpreter()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted(3)",
            "End Sub",
            "Private Function Counted(ByVal N As Long) As Long",
            "    Dim C As Collection, I As Long",
            "    Set C = New Collection",
            "    For I = 1 To N",
            "        C.Add I",
            "    Next",
            "    Counted = C.Count",
            "End Function"), Classes, expected: "| 3 |");
}
