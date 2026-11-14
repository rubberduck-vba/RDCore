using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The Err object after a procedure that met an error has returned (<strong>MS-VBAL §5.4.4.1</strong>, <strong>§5.4.4.2</strong>,
/// <strong>§6.1.3.2.1.1</strong>): an <c>On Error</c> statement and a <c>Resume</c> reset it, and so does leaving the procedure while its handler runs. A
/// procedure that resumed past an error and returned leaves the error in Err, an <c>Exit Sub</c> included, in both engines, as MS-VBA does (observed in
/// Excel). A <c>Raise</c> with no description after it keeps the description of the error Err holds: values of Err that have not been cleared serve as the
/// values of the new error (<strong>§6.1.3.2.1.2</strong>).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.4 Error Handling Statements")]
public sealed class ErrAfterReturnTests
{
    private const string Procedures =
        "Private Sub ResumedEndSub()\r\n    On Error Resume Next\r\n    Dim z As Long\r\n    z = 1 / 0\r\nEnd Sub\r\n" +
        "Private Sub ResumedExitSub()\r\n    On Error Resume Next\r\n    Dim z As Long\r\n    z = 1 / 0\r\n    Exit Sub\r\nEnd Sub\r\n" +
        "Private Function ResumedEndFunction() As Long\r\n    On Error Resume Next\r\n    ResumedEndFunction = 1 / 0\r\nEnd Function\r\n" +
        "Private Sub HandlerResumeNext()\r\n    On Error GoTo H\r\n    Dim z As Long\r\n    z = 1 / 0\r\n    Exit Sub\r\nH:\r\n    Resume Next\r\nEnd Sub\r\n" +
        "Private Sub HandlerExitSub()\r\n    On Error GoTo H\r\n    Dim z As Long\r\n    z = 1 / 0\r\n    Exit Sub\r\nH:\r\n    Exit Sub\r\nEnd Sub\r\n" +
        "Private Sub HandlerEndSub()\r\n    On Error GoTo H\r\n    Dim z As Long\r\n    z = 1 / 0\r\nH:\r\nEnd Sub\r\n" +
        "Private Sub ResumedThenOnErrorGoTo0()\r\n    On Error Resume Next\r\n    Dim z As Long\r\n    z = 1 / 0\r\n    On Error GoTo 0\r\nEnd Sub\r\n" +
        "Private Sub NoErrorAtAll()\r\n    Dim z As Long\r\n    z = 1\r\nEnd Sub\r\n";

    private static string Program(params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + Procedures + "Public Sub Main()\r\n" + string.Join("\r\n", body) + "\r\nEnd Sub\r\n";

    private static async Task Expect(string[] expected, string program, VBExecutionEngine engine)
        => Assert.AreEqual(string.Join(" | ", expected), string.Join(" | ", await RunAsync([], program, engine)));

    private static string After(string call) => $"    {call}\r\n    Debug.Print Err.Number & \" [\" & Err.Description & \"]\"";

    // a procedure that resumed past an error returns with the error still in Err, whether it ends at its End Sub, an Exit Sub or an End Function.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AProcedureThatResumedPastAnError_ReturnsWithTheErrorInErr(VBExecutionEngine engine)
        => await Expect(["11 [Division by zero]", "11 [Division by zero]", "11 [Division by zero]"], Program(
            After("ResumedEndSub"),
            After("ResumedExitSub"),
            "    Dim r As Long",
            After("r = ResumedEndFunction()")), engine);

    // a Resume, and an On Error statement, reset Err before the procedure returns.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AResumeAndAnOnErrorStatement_ResetErr_BeforeTheProcedureReturns(VBExecutionEngine engine)
        => await Expect(["0 []", "0 []"], Program(
            After("HandlerResumeNext"),
            After("ResumedThenOnErrorGoTo0")), engine);

    // a Raise with no description keeps the description of the error Err holds, which has not been cleared (MS-VBAL §6.1.3.2.1.2): Err.Raise 13 is
    // described "Division by zero", not "Type mismatch".
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task ARaiseAfterAProcedureThatResumedPastAnError_KeepsItsDescription(VBExecutionEngine engine)
        => await Expect(["before 11", "raised 13 [Division by zero]"], Program(
            "    On Error Resume Next",
            "    ResumedEndSub",
            "    Debug.Print \"before \" & Err.Number",
            "    Err.Raise 13",
            "    Debug.Print \"raised \" & Err.Number & \" [\" & Err.Description & \"]\""), engine);

    // a call of a procedure that meets no error leaves Err as it was.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task ACallThatMeetsNoError_LeavesErrAsItWas(VBExecutionEngine engine)
        => await Expect(["11"], Program(
            "    On Error Resume Next",
            "    Dim z As Long",
            "    z = 1 / 0",
            "    NoErrorAtAll",
            "    Debug.Print Err.Number"), engine);

    // a procedure left while its handler runs, by an Exit Sub or at its End Sub, returns with Err reset (MS-VBA, observed in Excel).
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AProcedureLeftWhileItsHandlerRuns_ReturnsWithErrReset(VBExecutionEngine engine)
        => await Expect(["0 []", "0 []"], Program(
            After("HandlerExitSub"),
            After("HandlerEndSub")), engine);
}
