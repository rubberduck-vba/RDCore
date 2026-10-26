using RDCore.SDK.Platform.Protocol;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An error a <c>Class_Terminate</c> leaves unhandled ends the program: MS-VBA reports it as an unhandled run-time error whatever <c>On Error</c> is in
/// effect where the object was let go of - <c>On Error Resume Next</c>, a handler of the caller's - and whatever let go of it: a <c>Set</c>, a
/// reassignment, a local at the end of its procedure, an <c>Erase</c>, a collection let go of. One the handler handles is no error to anything (recorded in
/// Excel, grid <c>oracle\s44</c> T01-T15, U05).
/// </summary>
[TestClass]
public sealed class TerminateErrorTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
    [
        ("Doomed", ClassModule("Doomed",
            "Public Name As String",
            "Private Sub Class_Terminate()",
            "    Debug.Print \"T\" & Name",
            "    If Program.TermErr <> 0 Then Err.Raise Program.TermErr",
            "    Debug.Print \"after\"",
            "End Sub")),
        ("Handled", ClassModule("Handled",
            "Private Sub Class_Terminate()",
            "    On Error GoTo h",
            "    Err.Raise 7",
            "    Exit Sub",
            "h:",
            "    Debug.Print \"handled\" & Err.Number",
            "End Sub")),
    ];

    private static async Task<ExecuteSessionResult> Run(string declarations, params string[] body)
    {
        ExecuteSessionResult? outcome = null;
        await InspectAsync(Classes,
            $"Attribute VB_Name = \"Program\"\r\nPublic TermErr As Long\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n",
            (_, result, _) =>
            {
                outcome = result;
                return Task.CompletedTask;
            });
        return outcome!;
    }

    private static void EndsTheProgramWith(int number, ExecuteSessionResult result, params string[] printed)
    {
        Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, result.ErrorMessage);
        Assert.AreEqual(number, result.ErrorNumber);
        CollectionAssert.AreEqual(printed, result.Output.ToArray());
    }

    [TestMethod]
    public async Task ATerminateThatRaises_EndsTheProgram_PastOnErrorResumeNext()
        => EndsTheProgramWith(5, await Run("",
            "On Error Resume Next", "TermErr = 5", "Dim d As Doomed", "Set d = New Doomed", "d.Name = \"a\"", "Set d = Nothing", "Debug.Print \"next\""), "Ta");

    [TestMethod]
    public async Task ATerminateThatRaises_EndsTheProgram_PastTheCallersHandler()
        => EndsTheProgramWith(9, await Run(
            "Private Sub Inner()\r\nOn Error GoTo h\r\nDim d As Doomed\r\nSet d = New Doomed\r\nd.Name = \"i\"\r\nSet d = Nothing\r\nDebug.Print \"next\"\r\nExit Sub\r\nh:\r\nDebug.Print \"caught\"\r\nEnd Sub",
            "On Error Resume Next", "TermErr = 9", "Inner", "Debug.Print \"back\""), "Ti");

    [TestMethod]
    public async Task ATerminateThatRaises_AsALocalGoesAway_EndsTheProgram()
        => EndsTheProgramWith(5, await Run(
            "Private Sub MakeAndDrop()\r\nDim d As Doomed\r\nSet d = New Doomed\r\nd.Name = \"L\"\r\nEnd Sub",
            "On Error Resume Next", "TermErr = 5", "MakeAndDrop", "Debug.Print \"back\""), "TL");

    [TestMethod]
    public async Task ATerminateThatRaises_AsAnArrayIsErased_EndsTheProgram()
        => EndsTheProgramWith(5, await Run("",
            "On Error Resume Next", "TermErr = 5", "Dim a(1) As Doomed", "Set a(0) = New Doomed", "a(0).Name = \"x\"", "Erase a", "Debug.Print \"next\""), "Tx");

    [TestMethod]
    public async Task ATerminateThatHandlesItsError_IsNoError_AndLeavesNoErr()
    {
        var result = await Run("", "On Error Resume Next", "Dim h As Handled", "Set h = New Handled", "Set h = Nothing", "Debug.Print Err.Number");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "handled7", "0" }, result.Output.Select(line => line.Trim()).ToArray(), string.Join("/", result.Output));
    }

    [TestMethod]
    public async Task ATerminateThatRaisesNothing_LetsTheProgramGoOn()
    {
        var result = await Run("", "TermErr = 0", "Dim d As Doomed", "Set d = New Doomed", "d.Name = \"a\"", "Set d = Nothing", "Debug.Print \"next\"");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "Ta", "after", "next" }, result.Output.ToArray());
    }
}
