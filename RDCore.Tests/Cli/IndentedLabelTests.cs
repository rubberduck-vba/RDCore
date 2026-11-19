using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.4.1.1: a statement label is defined at the start of a logical line, and whitespace is no token of the syntactic grammar (§3.3): a label
/// indented by it is a label, as MS-VBA has it (observed in Excel). The VBE moves a label to the first column; a module
/// written in any other editor, or typed at the BASIC prompt, may not.
/// </summary>
[TestClass]
public sealed class IndentedLabelTests
{
    [TestMethod]
    public async Task AnIndentedLabel_IsALabel()
        => CollectionAssert.AreEqual(new[] { "ok" }, await RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    GoTo Skip\r\n    Debug.Print \"skipped\"\r\n    Skip:\r\n    Debug.Print \"ok\"\r\nEnd Sub\r\n"));

    [TestMethod]
    public async Task AnIndentedLabel_IsALabel_InBasic()
    {
        var result = await SupportedLanguageTests.RunAsync(expression: "", statement: "    GoTo Skip\r\n    Print \"skipped\"\r\n    Skip:\r\n    Print \"ok\"",
            language: SupportedLanguages.BASIC);

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "ok" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task AnIndentedLabel_IsTheTargetOfAnErrorHandler()
        => CollectionAssert.AreEqual(new[] { "h5" }, await RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    On Error GoTo Handler\r\n    Err.Raise 5\r\n    Exit Sub\r\n"
            + "    Handler:\r\n    Debug.Print \"h\" & Err.Number\r\nEnd Sub\r\n"));
}
