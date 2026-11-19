using RDCore.SDK.Platform.Protocol;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>RD-VBAL §5.4.2.9</strong>: <c>If x Then:</c> at the end of a line is a single-line <c>If</c> with no statement, as vb6.exe and MS-VBA read it.
/// An <c>End If</c> after it closes no block (<strong>MS-VBAL §5.4.2.8</strong> takes the end of the line after <c>Then</c>, and a <c>:</c> is not one).
/// </summary>
[TestClass]
public sealed class IfThenColonTests
{
    private static string Program(params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    // the shape vb6.exe was measured to run: it prints A, whatever the condition, the If having no statement.
    [TestMethod]
    [DataRow("True", DisplayName = "a true condition")]
    [DataRow("False", DisplayName = "a false condition")]
    public async Task AnIfWithNoStatement_ThenTheNextLine_RunsTheNextLine(string condition)
    {
        var program = Program($"    If {condition} Then:", "    Debug.Print \"A\"");

        Assert.IsEmpty(await LoadErrorsAsync([], program));
        CollectionAssert.AreEqual(new[] { "A" }, await RunAsync([], program));
    }

    // MS-VBA refuses to compile it, with the End If's error.
    [TestMethod]
    [DataRow("True", DisplayName = "a true condition")]
    [DataRow("False", DisplayName = "a false condition")]
    public async Task AnIfWithNoStatement_ThenAnEndIf_IsRefused_AsAnEndIfWithoutBlockIf(string condition)
    {
        var result = await RunWithSyntaxErrorsAsync(Program($"    If {condition} Then:", "        Debug.Print \"A\"", "    End If", "    Debug.Print \"B\""));

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        Assert.IsEmpty(result.Output);
        var endIfWithoutBlockIf = RDCore.Runtime.LanguageCore.Exceptions.VBCompileError_EndIfWithoutBlockIf;
        Assert.IsTrue(result.Diagnostics!.Any(error => error.StartsWith(endIfWithoutBlockIf, StringComparison.Ordinal)),
            string.Join("; ", result.Diagnostics!));
    }
}
