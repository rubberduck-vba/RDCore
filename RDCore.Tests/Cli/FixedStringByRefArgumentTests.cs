using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A fixed-length <c>String</c> variable given to a <c>ByRef String</c> parameter (<strong>MS-VBAL §5.3.1.11</strong>): the parameter is a <c>String</c> of its own
/// while the call runs, and the variable is let what it holds when the call is over, fitted to its length. Both engines do so, as MS-VBA does (observed in
/// Excel).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Parameter Binding")]
public sealed class FixedStringByRefArgumentTests
{
    private static string Program(params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Private Type TF\r\n    F As String * 3\r\nEnd Type\r\n" +
            "Private G As String * 3\r\n" +
            "Private Sub Grow(ByRef S As String)\r\n    S = \"abcdef\"\r\n    S = S & \"g\"\r\nEnd Sub\r\n" +
            "Private Sub Shrink(ByRef S As String)\r\n    S = \"z\"\r\nEnd Sub\r\n" +
            "Private Sub GrowErr(ByRef S As String)\r\n    S = \"abcdef\"\r\n    Err.Raise 5\r\nEnd Sub\r\n" +
            "Private Sub GrowSee(ByRef S As String)\r\n    S = \"abcdef\"\r\n    Debug.Print \"in \" & Len(S) & \" [\" & S & \"] g[\" & G & \"]\"\r\nEnd Sub\r\n" +
            "Private Function Pre(ByRef S As String) As String\r\n    S = \"b\" & S\r\n    Pre = S\r\nEnd Function\r\n" +
            "Public Sub Main()\r\n" + string.Join("\r\n", body) + "\r\nEnd Sub\r\n";

    private static async Task Expect(string[] expected, string program, VBExecutionEngine engine)
        => Assert.AreEqual(string.Join(" | ", expected), string.Join(" | ", await RunAsync([], program, engine)));

    // what the callee assigns is cut to the variable's length, and what is shorter is padded with spaces.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AFixedLengthString_TakesWhatTheCalleeAssigned_FittedToItsLength(VBExecutionEngine engine)
        => await Expect(["[abc] 3", "[z  ] 3"], Program(
            "    Dim F As String * 3, H As String * 3",
            "    Grow F",
            "    Debug.Print \"[\" & F & \"] \" & Len(F)",
            "    H = \"abc\"",
            "    Shrink H",
            "    Debug.Print \"[\" & H & \"] \" & Len(H)"), engine);

    // the parameter is a String of its own while the call runs: it is as long as what it is assigned, and the variable is not written until the call is over.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task TheParameter_IsAStringOfItsOwn_WhileTheCallRuns(VBExecutionEngine engine)
        => await Expect(["in 6 [abcdef] g[xyz]", "[abc] 3"], Program(
            "    G = \"xyz\"",
            "    GrowSee G",
            "    Debug.Print \"[\" & G & \"] \" & Len(G)"), engine);

    // a field of a record and an element of an array are passed the same way.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AFieldAndAnElement_TakeWhatTheCalleeAssigned_FittedToTheirLength(VBExecutionEngine engine)
        => await Expect(["[abc] 3", "[abc] 3"], Program(
            "    Dim T As TF, E(1 To 2) As String * 3",
            "    Grow T.F",
            "    Debug.Print \"[\" & T.F & \"] \" & Len(T.F)",
            "    Grow E(1)",
            "    Debug.Print \"[\" & E(1) & \"] \" & Len(E(1))"), engine);

    // an operand left of the call is read at its place in the expression, before the call: it is what the variable held then, and once the call returns
    // the variable holds what the callee assigned (MS-VBA, observed in Excel for a variable; the field and the element are expected to match it).
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AnOperandLeftOfTheCall_IsWhatTheVariableHeldBeforeIt(VBExecutionEngine engine)
        => await Expect(["[a  ba  ] [ba ]", "[a  ba  ] [ba ]", "[a  ba  ] [ba ]"], Program(
            "    Dim F As String * 3, T As TF, E(1 To 2) As String * 3, R As String",
            "    F = \"a\": T.F = \"a\": E(1) = \"a\"",
            "    R = F & Pre(F)",
            "    Debug.Print \"[\" & R & \"] [\" & F & \"]\"",
            "    R = T.F & Pre(T.F)",
            "    Debug.Print \"[\" & R & \"] [\" & T.F & \"]\"",
            "    R = E(1) & Pre(E(1))",
            "    Debug.Print \"[\" & R & \"] [\" & E(1) & \"]\""), engine);

    // a call that raises an error does not return, and the variable keeps what it held.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task ACallThatRaisesAnError_LeavesTheVariableAsItWas(VBExecutionEngine engine)
        => await Expect(["err 5", "[xyz]"], Program(
            "    Dim F As String * 3",
            "    On Error Resume Next",
            "    F = \"xyz\"",
            "    GrowErr F",
            "    Debug.Print \"err \" & Err.Number",
            "    Debug.Print \"[\" & F & \"]\""), engine);

    // a reference parameter given a scalar field of a record is a second name for the field: a Let of the field through the record is what it reads after,
    // and what it is assigned is the field's (MS-VBA, observed in Excel).
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AParameterGivenAScalarField_ReadsALetOfTheFieldThroughTheRecord(VBExecutionEngine engine)
        => await Expect(["N 4", "main 104"],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Private Type TX\r\n    X As Long\r\nEnd Type\r\n" +
            "Private M As TX\r\n" +
            "Private Sub Inner(ByRef N As Long)\r\n" +
            "    M.X = M.X + 3\r\n" +
            "    Debug.Print \"N \" & N\r\n" +
            "    N = N + 100\r\n" +
            "End Sub\r\n" +
            "Public Sub Main()\r\n" +
            "    M.X = 1\r\n" +
            "    Inner M.X\r\n" +
            "    Debug.Print \"main \" & M.X\r\n" +
            "End Sub\r\n", engine);
}
