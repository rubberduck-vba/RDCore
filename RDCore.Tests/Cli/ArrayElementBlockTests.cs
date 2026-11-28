using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// The elements of an array of a fixed-width type: each holds every value of its type, and an element given by reference (MS-VBAL §5.3.1.11) is the
/// element itself, which reads what the array is let, erased or copied into it, and which no copy of the array shares. Each case runs in either engine.
/// </summary>
[TestClass]
public sealed class ArrayElementBlockTests
{
    private static string ProgramOf(params string[] lines)
        => string.Join("\r\n", ["Attribute VB_Name = \"Program\"", "Option Explicit", .. lines]) + "\r\n";

    private static async Task AssertPrintsAsync(string program, params string[] printed)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            CollectionAssert.AreEqual(printed, await ModuleWorkspace.RunAsync([], program, engine), $"{engine}: {program}");
        }
    }

    // the least and the greatest value of each type, or the ones nearest zero where a floating-point type has them, assigned to two elements and read back
    // as text; the element between them is never assigned and holds the type's default.
    [TestMethod]
    [DataRow("Byte", "0", "255", "0", "255", "0")]
    [DataRow("Integer", "-32768", "32767", "-32768", "32767", "0")]
    [DataRow("Long", "-2147483648", "2147483647", "-2147483648", "2147483647", "0")]
    [DataRow("LongLong", "CLngLng(\"-9223372036854775808\")", "CLngLng(\"9223372036854775807\")", "-9223372036854775808", "9223372036854775807", "0")]
    [DataRow("Single", "-3.402823E+38", "1.401298E-45", "-3.402823E+38", "1.401298E-45", "0")]
    [DataRow("Double", "-1.7976931348623E+308", "4.94065645841247E-324", "-1.7976931348623E+308", "4.94065645841247E-324", "0")]
    [DataRow("Currency", "CCur(\"-922337203685477.5808\")", "CCur(\"922337203685477.5807\")", "-922337203685477.5808", "922337203685477.5807", "0")]
    [DataRow("Boolean", "True", "False", "True", "False", "False")]
    public Task AnElement_HoldsEveryValueOfItsType(string type, string least, string greatest, string leastText, string greatestText, string defaultText)
        => AssertPrintsAsync(ProgramOf(
            "Public Sub Main()",
            $"    Dim A(1 To 3) As {type}",
            $"    A(1) = {least}",
            $"    A(3) = {greatest}",
            "    Debug.Print \"[\" & A(1) & \"|\" & A(2) & \"|\" & A(3) & \"]\"",
            "End Sub"), $"[{leastText}|{defaultText}|{greatestText}]");

    // a Date is held as its serial: 1 January 100 is -657434, and one second before the year 10000 is 2958465 and 86399/86400 of a day.
    [TestMethod]
    public Task ADateElement_AndAnEnumElement_HoldEveryValueOfTheirType()
        => AssertPrintsAsync(ProgramOf(
            "Private Enum Signed",
            "    Lowest = -2147483648",
            "    Highest = 2147483647",
            "End Enum",
            "Public Sub Main()",
            "    Dim D(1 To 3) As Date, E(1 To 3) As Signed",
            "    D(1) = #1/1/100#",
            "    D(3) = #12/31/9999 11:59:59 PM#",
            "    E(1) = Lowest",
            "    E(3) = Highest",
            "    Debug.Print Year(D(1)) & \"|\" & CDbl(D(1)) & \"|\" & CDbl(D(2)) & \"|\" & CDbl(D(3)) & \"|\" & Year(D(3))",
            "    Debug.Print E(1) & \"|\" & E(2) & \"|\" & E(3)",
            "End Sub"), "100|-657434|0|2958465.99998843|9999", "-2147483648|0|2147483647");

    // a fixed-size array field is let in place when its record is (MS-VBAL §5.4.3.8): the element given by reference reads what the array was let.
    [TestMethod]
    public Task AnElementGivenByReference_ReadsWhatItsArrayIsLet_AndWhatItIsAssignedTheArrayHolds()
        => AssertPrintsAsync(ProgramOf(
            "Private Type THolder",
            "    V(1 To 3) As Long",
            "End Type",
            "Public Sub Main()",
            "    Dim H As THolder, G As THolder",
            "    H.V(2) = 3",
            "    G.V(2) = 7",
            "    Watch H.V(2), H, G",
            "    Debug.Print H.V(2) & \"|\" & G.V(2)",
            "End Sub",
            "Private Sub Watch(ByRef N As Long, ByRef H As THolder, ByRef G As THolder)",
            "    Debug.Print N",
            "    H = G",
            "    Debug.Print N",
            "    N = 9",
            "End Sub"), "3", "7", "9|7");

    // Erase gives each element of a fixed-size array its default value where it is (MS-VBAL §5.4.3.5): an element given by reference before reads it, and
    // is given by reference again after.
    [TestMethod]
    public Task AnElementGivenByReferenceBefore_ReadsTheDefault_AfterItsArrayIsErased()
        => AssertPrintsAsync(ProgramOf(
            "Public Sub Main()",
            "    Dim A(1 To 3) As Double",
            "    Bump A(2)",
            "    Erase A",
            "    Debug.Print \"[\" & A(2) & \"]\"",
            "    Bump A(2)",
            "    Debug.Print \"[\" & A(2) & \"]\"",
            "End Sub",
            "Private Sub Bump(ByRef N As Double)",
            "    N = N + 2.25",
            "End Sub"), "[0]", "[2.25]");

    // an array assigned is a copy of its elements (MS-VBAL §5.4.3.8): an element given by reference is the element of one array, not of the other.
    [TestMethod]
    public Task AnElementGivenByReference_IsTheElementOfOneArray_AndNoCopyOfItShares()
        => AssertPrintsAsync(ProgramOf(
            "Public Sub Main()",
            "    Dim A() As Integer, B() As Integer",
            "    ReDim A(1 To 3)",
            "    A(2) = 5",
            "    B = A",
            "    Bump B(2)",
            "    Debug.Print A(2) & \"|\" & B(2)",
            "    Keep A(2), A",
            "    Debug.Print A(2) & \"|\" & B(2)",
            "End Sub",
            "Private Sub Bump(ByRef N As Integer)",
            "    N = N + 1",
            "End Sub",
            "Private Sub Keep(ByRef N As Integer, ByRef A() As Integer)",
            "    Dim C() As Integer",
            "    C = A",
            "    N = 8",
            "    Debug.Print C(2) & \"|\" & A(2)",
            "End Sub"), "5|6", "5|8", "8|6");

    // two parameters given one element are bound to the same element: what one is assigned, the other reads.
    [TestMethod]
    public Task TwoParametersGivenOneElement_AreTheSameElement()
        => AssertPrintsAsync(ProgramOf(
            "Public Sub Main()",
            "    Dim A(0 To 2) As Byte",
            "    Twice A(1), A(1)",
            "    Debug.Print A(1)",
            "End Sub",
            "Private Sub Twice(ByRef X As Byte, ByRef Y As Byte)",
            "    X = 200",
            "    Debug.Print Y",
            "    Y = Y + 1",
            "    Debug.Print X",
            "End Sub"), "200", "201", "201");
}
