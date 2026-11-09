using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Property Get</c> of an object variable declared as a class, read by compiled code: for the whole value a variable is given, as an operand, in a
/// condition, given arguments, through an interface the object's class implements, or as a member of the object of a <c>With</c> block. Compiled code
/// finds the member the object's class invokes for it as the interpreter finds it, and calls it as the interpreter does, the object given as <c>Me</c>. A
/// read of a <c>Property Get</c> of <c>Nothing</c> is the interpreter's, which raises error 91. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledPropertyReadTests
{
    // each row folds what each read gives into the number it prints, in order: a read of the wrong object, of the wrong member, or with the wrong argument
    // is another number.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Point", ModuleWorkspace.ClassModule("Point", "Option Explicit",
            "Private mX As Long",
            "Private mY As Long",
            "Public Sub Move(ByVal ToX As Long, ByVal ToY As Long)",
            "    mX = ToX",
            "    mY = ToY",
            "End Sub",
            "Public Property Get X() As Long",
            "    X = mX",
            "End Property",
            "Public Property Get Y() As Long",
            "    Y = mY",
            "End Property",
            "Public Property Get Item(ByVal Index As Long) As Long",
            "    Item = mX * Index + mY",
            "End Property",
            "Public Property Get IsFar() As Boolean",
            "    IsFar = mX + mY > 10",
            "End Property",
            "Public Property Get Ratio() As Double",
            "    Ratio = mX / 4",
            "End Property",
            "Public Property Get Tick() As Long",
            "    mY = mY + 1",
            "    Tick = mY",
            "End Property",
            "Public Property Get Failing() As Long",
            "    Failing = mX",
            "    If mX > 1 Then Err.Raise 5",
            "End Property",
            "Public Property Get Boxed() As Variant",
            "    Boxed = mX * 2",
            "End Property")),
        ("IShape", ModuleWorkspace.ClassModule("IShape", "Option Explicit",
            "Public Property Get Area() As Long",
            "End Property")),
        ("Square", ModuleWorkspace.ClassModule("Square", "Option Explicit",
            "Implements IShape",
            "Private mSide As Long",
            "Public Sub Init(ByVal Side As Long)",
            "    mSide = Side",
            "End Sub",
            "Private Property Get IShape_Area() As Long",
            "    IShape_Area = mSide * mSide",
            "End Property")),
        ("Strip", ModuleWorkspace.ClassModule("Strip", "Option Explicit",
            "Implements IShape",
            "Private mLength As Long",
            "Public Sub Init(ByVal Length As Long)",
            "    mLength = Length",
            "End Sub",
            "Private Property Get IShape_Area() As Long",
            "    IShape_Area = mLength * 3 + 1",
            "End Property")),
    ];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the program run both ways, as AssertAlikeAsync runs it, with as many statements stepped as recoveries says; and each call compiled code made,
    // made through the member's typed entry: none through its binding.
    private static async Task AssertReadAsync(string program, string compiled, long direct, long recoveries = 0, string? expected = null)
    {
        var result = await AssertAlikeAsync(program, compiled, recoveries, classes: Classes, expected: expected);
        CompiledDirectCallTests.AssertCalls(direct, (result.Body!.DirectCalls, result.Body.IndirectCalls));
    }

    // a Property Get read for the whole value a variable is given, and another as an operand of the value the function result is given.
    [TestMethod]
    public async Task APropertyRead_ForTheWholeValue_OrAsAnOperand_IsCompiled()
        => await AssertReadAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Sum(4)",
            "End Sub",
            "Private Function Sum(ByVal N As Long) As Long",
            "    Dim P As Point",
            "    Dim I As Long",
            "    Dim V As Long",
            "    Set P = New Point",
            "    For I = 1 To N",
            "        P.Move I, I * 2",
            "        V = P.X",
            "        Sum = Sum * 3 + V * 10 + P.Y",
            "    Next",
            "End Function"), "Sum", direct: 12, expected: "696");

    // a Property Get given an argument, a Boolean one in a condition, and a Double one as an operand.
    [TestMethod]
    public async Task APropertyRead_GivenAnArgument_InACondition_OrOfAnotherKind_IsCompiled()
        => await AssertReadAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Mixed(6)",
            "End Sub",
            "Private Function Mixed(ByVal N As Long) As Double",
            "    Dim P As Point",
            "    Dim I As Long",
            "    Set P = New Point",
            "    For I = 1 To N",
            "        P.Move I * 2, 3",
            "        If P.IsFar Then",
            "            Mixed = Mixed * 2 + P.Item(I)",
            "        Else",
            "            Mixed = Mixed - P.Ratio",
            "        End If",
            "    Next",
            "End Function"), "Mixed", direct: 18);

    // a Property Get of an interface, read on objects of two classes that implement it: each read is of the one the object's class implements it with.
    [TestMethod]
    public async Task APropertyOfAnInterface_IsTheOneTheObjectsClassImplementsItWith()
        => await AssertReadAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Areas(4)",
            "End Sub",
            "Private Function Areas(ByVal N As Long) As Long",
            "    Dim S As IShape",
            "    Dim Sq As Square",
            "    Dim St As Strip",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        If I Mod 2 = 0 Then",
            "            Set Sq = New Square",
            "            Sq.Init I",
            "            Set S = Sq",
            "        Else",
            "            Set St = New Strip",
            "            St.Init I",
            "            Set S = St",
            "        End If",
            "        Areas = Areas * 5 + S.Area",
            "    Next",
            "End Function"), "Areas", direct: 8);

    // two reads of a Property Get that changes the object, in one expression: made once each, left to right.
    [TestMethod]
    public async Task TwoReadsInOneExpression_AreMadeOnceEach_LeftToRight()
        => await AssertReadAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Ticks(2)",
            "End Sub",
            "Private Function Ticks(ByVal N As Long) As Long",
            "    Dim P As Point",
            "    Dim I As Long",
            "    Set P = New Point",
            "    For I = 1 To N",
            "        Ticks = Ticks * 100 + P.Tick + P.Tick * 10",
            "    Next",
            "End Function"), "Ticks", direct: 4, expected: "2143");

    // a Property Get of the Variant type, its result assigned to a Variant.
    [TestMethod]
    public async Task AVariantProperty_IsAssignedToAVariant()
        => await AssertReadAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Boxes(3)",
            "End Sub",
            "Private Function Boxes(ByVal N As Long) As Long",
            "    Dim P As Point",
            "    Dim I As Long",
            "    Dim V As Variant",
            "    Set P = New Point",
            "    For I = 1 To N",
            "        P.Move I, 0",
            "        V = P.Boxed",
            "        Boxes = Boxes * 10 + V",
            "    Next",
            "End Function"), "Boxes", direct: 6, expected: "246");

    // a Property Get of the object of a With block, for the whole value a variable is given. The With statement and its End With are the interpreter's.
    [TestMethod]
    public async Task APropertyOfAWithBlocksObject_IsReadForTheWholeValue()
        => await AssertReadAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Within(3)",
            "End Sub",
            "Private Function Within(ByVal N As Long) As Long",
            "    Dim P As Point",
            "    Dim I As Long",
            "    Dim V As Long",
            "    Set P = New Point",
            "    For I = 1 To N",
            "        P.Move I, I",
            "        With P",
            "            V = .X",
            "        End With",
            "        Within = Within * 10 + V",
            "    Next",
            "End Function"), "Within", direct: 6, recoveries: 6, expected: "123");

    // a read of a Property Get of Nothing raises error 91: to the handler, which resumes after it, and to the caller, with nothing to catch it. The
    // interpreter makes the read (no call is compiled code's), and so each error is the interpreter's: two; each read of Err.Number is compiled code's.
    [TestMethod]
    public async Task APropertyReadOfNothing_RaisesError91()
        => await AssertReadAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Handled(2)",
            "    Debug.Print Unhandled()",
            "End Sub",
            "Private Function Handled(ByVal N As Long) As Long",
            "    Dim P As Point",
            "    Dim I As Long",
            "    On Error Resume Next",
            "    For I = 1 To N",
            "        Handled = Handled + P.X",
            "        Handled = Handled * 100 + Err.Number",
            "        Err.Clear",
            "    Next",
            "End Function",
            "Private Function Unhandled() As Long",
            "    Dim P As Point",
            "    Unhandled = P.X + 1",
            "End Function"), "Handled", direct: 0, recoveries: 2, expected: "9191");

    // an error a Property Get raises is the caller's: to its handler, which resumes after the statement, the variable not assigned what the property was
    // given before it raised it. The two errors given to the handler are recoveries; the three reads of Err.Number are compiled code's.
    [TestMethod]
    public async Task AnErrorAPropertyRaises_IsTheCallersToHandle()
        => await AssertReadAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Raised(3)",
            "End Sub",
            "Private Function Raised(ByVal N As Long) As Long",
            "    Dim P As Point",
            "    Dim I As Long",
            "    Dim V As Long",
            "    Set P = New Point",
            "    On Error Resume Next",
            "    For I = 1 To N",
            "        P.Move I, 0",
            "        V = -1",
            "        V = P.Failing",
            "        Raised = Raised * 100 + V * 10 + Err.Number",
            "        Err.Clear",
            "    Next",
            "End Function"), "Raised", direct: 6, recoveries: 2, expected: "99495");
}
