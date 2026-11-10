using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The factory shape, compiled: a <c>Property Let</c> or <c>Property Set</c> of the object of a <c>With</c> block, or of an object variable, called by compiled
/// code with the value (MS-VBAL §5.4.3.8, §5.4.3.9), and the <c>End With</c> that lets go of the block's object (MS-VBAL §5.4.2.21), the interpreter's when
/// letting go of it destroys the object. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledFactoryTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Coord", ModuleWorkspace.ClassModule("Coord",
            "Private Type TCoord",
            "    X As Long",
            "    Y As Long",
            "    Partner As Coord",
            "End Type",
            "Private this As TCoord",
            "Public Property Get X() As Long",
            "    X = this.X",
            "End Property",
            "Public Property Let X(ByVal Value As Long)",
            "    Log = Log & \"x\" & Value & \";\"",
            "    this.X = Value",
            "End Property",
            "Public Property Get Y() As Long",
            "    Y = this.Y",
            "End Property",
            "Public Property Let Y(Value As Long)",
            "    this.Y = Value",
            "    Value = 0",
            "End Property",
            "Public Property Get Partner() As Coord",
            "    Set Partner = this.Partner",
            "End Property",
            "Public Property Set Partner(ByVal Value As Coord)",
            "    Set this.Partner = Value",
            "End Property",
            "Public Property Get Self() As Coord",
            "    Set Self = Me",
            "End Property",
            "Private Sub Class_Terminate()",
            "    Log = Log & \"gone \" & this.X & \";\"",
            "End Sub")),
        ("IShape", ModuleWorkspace.ClassModule("IShape",
            "Public Property Get Size() As Long",
            "End Property",
            "Public Property Let Size(ByVal Value As Long)",
            "End Property")),
        ("Square", ModuleWorkspace.ClassModule("Square",
            "Implements IShape",
            "Private mSize As Long",
            "Private Property Get IShape_Size() As Long",
            "    IShape_Size = mSize",
            "End Property",
            "Private Property Let IShape_Size(ByVal Value As Long)",
            "    Log = Log & \"s\" & Value & \";\"",
            "    mSize = Value",
            "End Property")),
    ];

    // the program run both ways, as AssertAlikeAsync runs it, with as many statements stepped as recoveries says; and as many calls made by compiled code as
    // calls says, directly or through the invoker, the Property Lets and Sets among them. Under the random walk-back the interpreter makes the calls after a
    // hand-over, and the count says only how many at most.
    private static async Task AssertFactoryAsync(string program, string expected, string procedure, long recoveries, long calls)
    {
        var result = await AssertAlikeAsync(program, procedure, recoveries, classes: Classes, expected: expected);
        var made = result.Body!.DirectCalls + result.Body.IndirectCalls;
        if (CompiledExecution.HandsOverAtRandom)
        {
            Assert.IsLessThanOrEqualTo(calls, made);
            return;
        }

        Assert.AreEqual(calls, made, $"{procedure}'s calls");
    }

    // the shape of a factory: a With block of an object made with New, its Property Lets called by compiled code, the object given back through a member of
    // it, and the End With done by compiled code, the function's result holding the object. The recoveries are the With of each of the 3 calls, and the
    // calls the 2 Property Lets of each; the Set of the result is a support call. Each object is destroyed when the next one is Set to the variable that held
    // it.
    [TestMethod]
    public async Task AFactory_CallsItsPropertyLets_AndEndsItsWithBlock_InCompiledCode()
        => await AssertFactoryAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Dim C As Coord",
            "    Dim K As Long",
            "    Dim S As String",
            "    For K = 1 To 3",
            "        Set C = Made(K, K * 10)",
            "        S = S & C.X & \"/\" & C.Y & \",\"",
            "    Next",
            "    Debug.Print S & Log",
            "End Sub",
            "Private Function Made(ByVal A As Long, ByVal B As Long) As Coord",
            "    With New Coord",
            "        .X = A",
            "        .Y = B",
            "        Set Made = .Self",
            "    End With",
            "End Function"), "1/10,2/20,3/30,x1;x2;gone 1;x3;gone 2;", "Made", recoveries: 3, calls: 6);

    // a Property Set of the object of a With block, called by compiled code with an object it holds. The recovery is the With, and the calls the Property
    // Let and the Property Set.
    [TestMethod]
    public async Task APropertySetOfTheObjectOfAWithBlock_IsCalledInCompiledCode()
        => await AssertFactoryAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Dim Q As Coord",
            "    Set Q = New Coord",
            "    Q.X = 7",
            "    Debug.Print Linked(Q).Partner.X & \" \" & Log",
            "End Sub",
            "Private Function Linked(ByVal Q As Coord) As Coord",
            "    With New Coord",
            "        .X = 1",
            "        Set .Partner = Q",
            "        Set Linked = .Self",
            "    End With",
            "End Function"), "7 x7;x1;", "Linked", recoveries: 1, calls: 2);

    // an End With that lets go of the only reference to its object: the interpreter runs it, and the object's Terminate runs there, before the next pass.
    // The recoveries are the With and its End With of each of the 3 passes, and the calls the Property Let of each.
    [TestMethod]
    public async Task AnEndWithThatDestroysItsObject_IsTheInterpreters_AndTerminateRunsThere()
        => await AssertFactoryAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Discarded",
            "    Debug.Print Log",
            "End Sub",
            "Private Sub Discarded()",
            "    Dim K As Long",
            "    For K = 1 To 3",
            "        With New Coord",
            "            .X = K",
            "        End With",
            "    Next",
            "End Sub"), "x1;gone 1;x2;gone 2;x3;gone 3;", "Discarded", recoveries: 6, calls: 3);

    // a With block of an object a variable holds, in a loop: compiled code ends it at each pass, the variable still holding the object. The recoveries
    // are the With of each of the 4 passes, and the calls the 2 Property Lets of each; the Set of the object made with New is a support call.
    [TestMethod]
    public async Task AWithBlockOfAnObjectAVariableHolds_IsEndedInCompiledCode()
        => await AssertFactoryAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Debug.Print Summed() & \" \" & Log",
            "End Sub",
            "Private Function Summed() As Long",
            "    Dim P As Coord",
            "    Dim K As Long",
            "    Set P = New Coord",
            "    For K = 1 To 4",
            "        With P",
            "            .X = K",
            "            .Y = K * 2",
            "        End With",
            "        Summed = Summed + K",
            "    Next",
            "End Function"), "10 x1;x2;x3;x4;gone 4;", "Summed", recoveries: 4, calls: 8);

    // the value of a Property Let is passed as a value, whatever its parameter's passing: what the Property Let writes to its parameter passed by reference
    // is not written to the variable the value was read from. The recovery is the With, and the calls the 3 Property Lets and the Property Get read.
    [TestMethod]
    public async Task APropertyLet_IsGivenTheValue_NotTheVariableItWasReadFrom()
        => await AssertFactoryAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Debug.Print Kept()",
            "End Sub",
            "Private Function Kept() As Long",
            "    Dim N As Long",
            "    Dim P As Coord",
            "    Set P = New Coord",
            "    N = 5",
            "    P.Y = N",
            "    With P",
            "        .Y = N + 1",
            "        .Y = N",
            "    End With",
            "    Kept = N * 100 + P.Y",
            "End Function"), "505", "Kept", recoveries: 1, calls: 4);

    // a Property Let of an object variable, in a loop: the Lets are compiled code's calls, the object the variable holds the one called on, one a pass.
    // There is nothing the interpreter runs alone; the Set of the object made with New is a support call.
    [TestMethod]
    public async Task APropertyLetOfAnObjectVariable_IsCalledInCompiledCode()
        => await AssertFactoryAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Debug.Print Filled() & \" \" & Log",
            "End Sub",
            "Private Function Filled() As Long",
            "    Dim P As Coord",
            "    Dim K As Long",
            "    Set P = New Coord",
            "    For K = 1 To 3",
            "        P.X = K * 2",
            "        Filled = Filled + K",
            "    Next",
            "End Function"), "6 x2;x4;x6;gone 6;", "Filled", recoveries: 0, calls: 3);

    // a Property Let of an object declared as an interface its class implements: the implementing Property Let is called, by compiled code, one a pass.
    // The Set of the object made with New is a support call.
    [TestMethod]
    public async Task APropertyLetThroughAnInterface_CallsTheImplementingPropertyLet()
        => await AssertFactoryAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Debug.Print Shaped() & \" \" & Log",
            "End Sub",
            "Private Function Shaped() As Long",
            "    Dim S As IShape",
            "    Dim K As Long",
            "    Set S = New Square",
            "    For K = 1 To 3",
            "        S.Size = K",
            "        Shaped = Shaped + K",
            "    Next",
            "End Function"), "6 s1;s2;s3;", "Shaped", recoveries: 0, calls: 3);

    // the value is evaluated before the object, as MS-VBA evaluates it: of a With block of Nothing, a value that fails is the value's error (11), and one that
    // does not is error 91 at the member, each resumed past. The recoveries are the With and the 2 errors; the 2 reads of Err.Number are compiled code's,
    // the call of Err.Clear is a support call, and the End With of a block of Nothing is compiled code's. Compiled code calls nothing: neither Property
    // Let is ever called.
    [TestMethod]
    public async Task AValueThatFails_OfAWithBlockOfNothing_IsTheValuesError()
        => await AssertFactoryAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Debug.Print Failed()",
            "End Sub",
            "Private Function Failed() As Long",
            "    Dim P As Coord",
            "    Dim D As Long",
            "    On Error Resume Next",
            "    With P",
            "        .X = 1 \\ D",
            "        Failed = Err.Number",
            "        Err.Clear",
            "        .X = 2",
            "        Failed = Failed * 100 + Err.Number",
            "    End With",
            "End Function"), "1191", "Failed", recoveries: 3, calls: 0);
}
