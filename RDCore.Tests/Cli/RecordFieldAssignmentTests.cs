using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A field of a record as the target of a <c>Let</c> (<strong>MS-VBAL §5.4.3.8</strong>) and of an <c>LSet</c> or <c>RSet</c>
/// (<strong>MS-VBAL §5.4.3.10</strong>, <strong>§5.4.3.11</strong>): it is assigned as a variable of its declared type is, in both engines.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
public sealed class RecordFieldAssignmentTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Defaulted", ClassModule("Defaulted", "Option Explicit",
            "Private m As Long",
            "Public Property Get Value() As Long",
            "Attribute Value.VB_UserMemId = 0",
            "    Value = m",
            "End Property",
            "Public Property Let Value(ByVal v As Long)",
            "Attribute Value.VB_UserMemId = 0",
            "    m = v",
            "End Property")),
    ];

    private static string Program(params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Private Type TQ\r\n    Item As Defaulted\r\n    S As String\r\n    F As String * 4\r\nEnd Type\r\n" +
            "Public Sub Main()\r\n" + string.Join("\r\n", body) + "\r\nEnd Sub\r\n";

    private static async Task Expect(string[] expected, Task<string[]> output)
        => Assert.AreEqual(string.Join(" | ", expected), string.Join(" | ", await output));

    // a Let of an object field that is not a Set assigns the object's default member, a Property Let, as a Let of an object variable does: the field
    // keeps its object.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task ALetOfAnObjectField_AssignsItsDefaultPropertyLet(VBExecutionEngine engine)
        => await Expect(["field 5 Defaulted", "variable 6"], RunAsync(Classes, Program(
            "    Dim Q As TQ, D As New Defaulted",
            "    Set Q.Item = New Defaulted",
            "    Q.Item = 5",
            "    Debug.Print \"field \" & Q.Item.Value & \" \" & TypeName(Q.Item)",
            "    D = 6",
            "    Debug.Print \"variable \" & D.Value"), engine));

    // an array of records whose element's field a ByRef parameter holds is locked while it does: a Let of another array to it is run-time error 10,
    // and the parameter still reads the field and writes it (MS-VBA, observed in Excel).
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AnArrayOfRecords_WhoseFieldIsPassedByReference_IsLocked(VBExecutionEngine engine)
        => await Expect(["inner err 10", "N 1", "q 101", "main 101 2"], RunAsync(Classes,
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Private Type TX\r\n    X As Long\r\nEnd Type\r\n" +
            "Private Q() As TX\r\nPrivate O() As TX\r\n" +
            "Private Sub Inner(ByRef N As Long)\r\n" +
            "    On Error Resume Next\r\n" +
            "    Q = O\r\n" +
            "    If Err.Number <> 0 Then Debug.Print \"inner err \" & Err.Number: Err.Clear\r\n" +
            "    Debug.Print \"N \" & N\r\n" +
            "    N = N + 100\r\n" +
            "    Debug.Print \"q \" & Q(1).X\r\n" +
            "End Sub\r\n" +
            "Public Sub Main()\r\n" +
            "    ReDim Q(1 To 2): ReDim O(1 To 3)\r\n" +
            "    Q(1).X = 1: O(1).X = 9\r\n" +
            "    Inner Q(1).X\r\n" +
            "    Debug.Print \"main \" & Q(1).X & \" \" & UBound(Q)\r\n" +
            "End Sub\r\n", engine));

    // LSet and RSet of a String field keep its length: the text is padded with spaces, or cut, to the length the field has.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task LSetAndRSetOfAStringField_KeepItsLength(VBExecutionEngine engine)
        => await Expect(["[x  ]", "[  y]", "[abc]", "[z   ]", "[   w]"], RunAsync(Classes, Program(
            "    Dim T As TQ",
            "    T.S = \"abc\"",
            "    LSet T.S = \"x\"",
            "    Debug.Print \"[\" & T.S & \"]\"",
            "    RSet T.S = \"y\"",
            "    Debug.Print \"[\" & T.S & \"]\"",
            "    LSet T.S = \"abcdef\"",
            "    Debug.Print \"[\" & T.S & \"]\"",
            "    LSet T.F = \"z\"",
            "    Debug.Print \"[\" & T.F & \"]\"",
            "    RSet T.F = \"w\"",
            "    Debug.Print \"[\" & T.F & \"]\""), engine));
}
