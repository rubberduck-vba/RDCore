using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// An object given to a procedure or a member compiled code calls (MS-VBAL §5.3.1.11): to a parameter passed by value, the object Set-coerced to the
/// parameter's type, which the callee's parameter holds, and counts, for as long as it runs; to one passed by reference, the caller's variable, which the
/// parameter is a second name for, so that what the callee Sets it to is what the caller holds after. Each object's <c>Terminate</c> runs when it is let go
/// of last. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledObjectArgumentTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Thing", ModuleWorkspace.ClassModule("Thing",
            "Option Explicit",
            "Public Value As Long",
            "Private Sub Class_Terminate()",
            "    Debug.Print \"terminated \" & Value",
            "End Sub")),
        ("Other", ModuleWorkspace.ClassModule("Other", "Option Explicit", "Public Value As Long")),
        ("Keeper", ModuleWorkspace.ClassModule("Keeper",
            "Option Explicit",
            "Private mKept As Thing",
            "Public Sub Keep(ByVal T As Thing)",
            "    Set mKept = T",
            "End Sub",
            "Public Sub Drop()",
            "    Set mKept = Nothing",
            "End Sub",
            "Public Sub Add(ByVal T As Thing, ByVal N As Long)",
            "    T.Value = T.Value + N",
            "End Sub",
            "Public Sub Twice()",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        Bump Me, I",
            "    Next",
            "End Sub",
            "Public Sub Hands()",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        Handed mKept, Me, I",
            "    Next",
            "End Sub")),
        ("IShape", ModuleWorkspace.ClassModule("IShape",
            "Option Explicit",
            "Public Sub Place(ByVal T As Thing, ByVal N As Long)",
            "End Sub")),
        ("Square", ModuleWorkspace.ClassModule("Square",
            "Option Explicit",
            "Implements IShape",
            "Private Sub IShape_Place(ByVal T As Thing, ByVal N As Long)",
            "    T.Value = T.Value + 10 * N",
            "End Sub")),
    ];

    private static readonly string Helpers = string.Join("\r\n",
        "Public Sub Add(ByVal T As Thing, ByVal N As Long)",
        "    T.Value = T.Value + N",
        "End Sub",
        "Public Sub Bump(ByVal K As Keeper, ByVal N As Long)",
        "    Debug.Print \"bump \" & N",
        "End Sub",
        "Public Sub Handed(ByVal T As Thing, ByVal K As Keeper, ByVal N As Long)",
        "    If N = 1 Then K.Drop",
        "    Debug.Print \"handed \" & (T Is Nothing)",
        "End Sub");

    // the program run both ways, as AssertAlikeAsync runs it, with nothing recovered, and each call compiled code made, made through the invoker or the
    // member's binding: a call given an object is never made directly, and one compiled code does not make is a support call, which it does not count.
    private static async Task AssertGivenAsync(string program, string compiled, long indirect, string expected)
    {
        var result = await AssertAlikeAsync(program, compiled, recoveries: 0, classes: Classes, expected: expected);
        if (!RDCore.Runtime.Compiled.CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual((0L, indirect), (result.Body!.DirectCalls, result.Body.IndirectCalls), $"{compiled}'s calls made directly, and not");
        }
    }

    // an object variable given by value to a procedure, at each turn of a loop: the procedure works on the object the variable refers to.
    [TestMethod]
    public async Task AnObjectGivenByValue_ToAProcedure_IsTheCallersObject()
        => await AssertGivenAsync(Program(
            "Public Sub Main()",
            "    Dim T As New Thing",
            "    Summed T",
            "    Debug.Print T.Value",
            "End Sub",
            "Private Sub Summed(ByVal T As Thing)",
            "    Dim I As Long",
            "    For I = 1 To 4",
            "        Add T, I",
            "    Next",
            "End Sub",
            Helpers), "Summed", indirect: 4, expected: "10 ; terminated 10");

    // an object given by value to a member of an object, and Nothing given by value to a procedure: the member is given the object, the procedure Nothing.
    [TestMethod]
    public async Task AnObjectGivenByValue_ToAMember_AndNothing_AreGivenAsTheyAre()
        => await AssertGivenAsync(Program(
            "Public Sub Main()",
            "    Dim T As New Thing, K As New Keeper",
            "    Summed K, T",
            "    Debug.Print T.Value",
            "End Sub",
            "Private Sub Summed(ByVal K As Keeper, ByVal T As Thing)",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        K.Add T, I",
            "        Told Nothing",
            "    Next",
            "End Sub",
            "Public Sub Told(ByVal T As Thing)",
            "    Debug.Print T Is Nothing",
            "End Sub",
            Helpers), "Summed", indirect: 6, expected: "True ; True ; True ; 6 ; terminated 6");

    // a member of an object declared as an interface, given an object: the member the object's class implements it with is given it.
    [TestMethod]
    public async Task AnObjectGiven_ToAMemberOfAnInterface_IsGivenToTheImplementation()
        => await AssertGivenAsync(Program(
            "Public Sub Main()",
            "    Dim T As New Thing, S As IShape",
            "    Set S = New Square",
            "    Placed S, T",
            "    Debug.Print T.Value",
            "End Sub",
            "Private Sub Placed(ByVal S As IShape, ByVal T As Thing)",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        S.Place T, I",
            "    Next",
            "End Sub",
            Helpers), "Placed", indirect: 3, expected: "60 ; terminated 60");

    // Me given by value, from a member of a class.
    [TestMethod]
    public async Task Me_GivenByValue_IsTheObjectTheMemberIsCalledOn()
        => await AssertGivenAsync(Program(
            "Public Sub Main()",
            "    Dim K As New Keeper",
            "    K.Twice",
            "    Debug.Print \"end\"",
            "End Sub",
            Helpers), "Twice", indirect: 2, expected: "bump 1 ; bump 2 ; end");

    // an object field of Me given by value: the callee's parameter holds the object while the callee runs, so the field let go of during the call, the
    // object is destroyed when the callee returns, after what it printed, and not before; the field holds Nothing the next time.
    [TestMethod]
    public async Task AnObjectFieldGivenByValue_IsHeldByTheCallee_UntilItReturns()
        => await AssertGivenAsync(Program(
            "Public Sub Main()",
            "    Dim K As New Keeper, T As Thing",
            "    Set T = New Thing",
            "    T.Value = 5",
            "    K.Keep T",
            "    Set T = Nothing",
            "    K.Hands",
            "    Debug.Print \"end\"",
            "End Sub",
            Helpers), "Hands", indirect: 2, expected: "handed False ; terminated 5 ; handed True ; end");

    // an object of one class, held by an Object variable, given by value to a parameter of another: error 13 at the call, as the interpreter raises it,
    // before the callee is entered, which prints nothing, and which compiled code never calls.
    [TestMethod]
    public async Task AnObjectOfAnotherClass_GivenByValue_IsATypeMismatch_AtTheCall()
        => await AssertGivenAsync(Program(
            "Public Sub Main()",
            "    Dim O As Object",
            "    Set O = New Other",
            "    Given O",
            "End Sub",
            "Private Sub Given(ByVal O As Object)",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        Entered O, I",
            "    Next",
            "End Sub",
            "Public Sub Entered(ByVal T As Thing, ByVal N As Long)",
            "    Debug.Print \"entered \" & N",
            "End Sub",
            Helpers), "Given", indirect: 0, expected: " |  | 13 ");

    // a module field given by value, which the callee lets go of: the callee's parameter is then the last that holds the object, which is destroyed when the
    // callee returns, after what it printed; the field gives Nothing the next time.
    [TestMethod]
    public async Task AModuleFieldGivenByValue_LetGoOfByTheCallee_IsDestroyedWhenTheCalleeReturns()
        => await AssertGivenAsync(Program(
            "Private G As Thing",
            "Public Sub Main()",
            "    Set G = New Thing",
            "    G.Value = 3",
            "    Handing",
            "    Debug.Print \"end\"",
            "End Sub",
            "Private Sub Handing()",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        Last G, I",
            "    Next",
            "End Sub",
            "Public Sub Last(ByVal T As Thing, ByVal N As Long)",
            "    Set G = Nothing",
            "    Debug.Print \"last \" & N & \" \" & (T Is Nothing)",
            "End Sub",
            Helpers), "Handing", indirect: 2, expected: "last 1 False ; terminated 3 ; last 2 True ; end");

    // object variables given by reference: the callee Sets its parameters, and the caller's variables hold what it Set them to.
    [TestMethod]
    public async Task ObjectVariablesGivenByReference_HoldWhatTheCalleeSetsThemTo()
        => await AssertGivenAsync(Program(
            "Public Sub Main()",
            "    Dim A As New Thing, B As New Thing",
            "    A.Value = 1",
            "    B.Value = 2",
            "    Debug.Print Swapped(A, B, A)",
            "End Sub",
            "Private Function Swapped(ByVal A As Thing, ByVal B As Thing, ByVal First As Thing) As Long",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        Swap A, B",
            "        If A Is First Then Swapped = Swapped * 10 + 1 Else Swapped = Swapped * 10 + 2",
            "    Next",
            "End Function",
            "Public Sub Swap(ByRef X As Thing, ByRef Y As Thing)",
            "    Dim Z As Thing",
            "    Set Z = X",
            "    Set X = Y",
            "    Set Y = Z",
            "End Sub",
            Helpers), "Swapped", indirect: 3, expected: "212 ;");

    // an object variable given to a parameter declared with neither ByVal nor ByRef, which is passed by reference: the callee Sets it to Nothing, and the
    // caller's variable holds Nothing after. The object, made for Main's statement, is held by it until it ends (MS-VBAL §5.3.1.10), and destroyed then.
    [TestMethod]
    public async Task AnObjectGivenByReference_SetToNothingByTheCallee_IsNothingInTheCaller()
        => await AssertGivenAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Released(New Thing)",
            "    Debug.Print \"end\"",
            "End Sub",
            "Private Function Released(ByVal T As Thing) As Long",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        Cleared T, I",
            "        If T Is Nothing Then Released = Released * 10 + 1 Else Released = Released * 10 + 2",
            "    Next",
            "End Function",
            "Public Sub Cleared(T As Thing, ByVal N As Long)",
            "    If N = 1 Then Set T = Nothing",
            "    Debug.Print \"cleared \" & N",
            "End Sub",
            Helpers), "Released", indirect: 2, expected: "cleared 1 ; cleared 2 ; 11 ; terminated 0 ; end");
}
