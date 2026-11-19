using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>RD-VBAL §5.4.2.9</strong>: <c>If x Then:</c> at the end of a line is a single-line <c>If</c> with no statement where no <c>End If</c> closes
/// a block, as vb6.exe runs it, and a block <c>If</c> (<strong>MS-VBAL §5.4.2.8</strong>) where one does.
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

    [TestMethod]
    [DataRow("True", "A|B", DisplayName = "a true condition")]
    [DataRow("False", "B", DisplayName = "a false condition")]
    public async Task ABlockIfClosedByEndIf_RunsItsBodyWhereTheConditionIsTrue(string condition, string expected)
    {
        var program = Program($"    If {condition} Then:", "        Debug.Print \"A\"", "    End If", "    Debug.Print \"B\"");

        CollectionAssert.AreEqual(expected.Split('|'), await RunAsync([], program));
    }
}
