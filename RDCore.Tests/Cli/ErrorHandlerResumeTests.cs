using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.4.4.1: an <c>On Error GoTo</c> sets the policy of the activation to go to its label, and an error caught by it runs the handler, which an
/// error inside it does not enter again. A <c>Resume</c> ends the handler, and the policy is still to go to the label: the next error goes to it again
/// (MS-VBA; observed in Excel).
/// </summary>
[TestClass]
public sealed class ErrorHandlerResumeTests
{
    private static async Task<string> Run(params string[] body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    " + string.Join("\r\n    ", body) + "\r\nEnd Sub\r\n";
        return string.Join("/", await RunAsync([], program));
    }

    [TestMethod]
    public async Task ASecondError_AfterTheHandlerResumedNext_GoesToTheHandlerAgain()
        => Assert.AreEqual("h5/between/h6/done", await Run(
            "On Error GoTo H", "Err.Raise 5", "Debug.Print \"between\"", "Err.Raise 6", "Debug.Print \"done\"", "Exit Sub",
            "H:", "Debug.Print \"h\" & Err.Number", "Resume Next"));

    [TestMethod]
    public async Task ASecondError_AfterTheHandlerResumedToALabel_GoesToTheHandlerAgain()
        => Assert.AreEqual("h5/between/h6/done", await Run(
            "On Error GoTo H", "Err.Raise 5", "Back:", "Debug.Print \"between\"", "Err.Raise 6", "Done:", "Debug.Print \"done\"", "Exit Sub",
            "H:", "Debug.Print \"h\" & Err.Number", "If Err.Number = 5 Then Resume Back", "Resume Done"));
}
