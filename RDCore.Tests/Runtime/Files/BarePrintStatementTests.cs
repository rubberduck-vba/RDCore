using RDCore.Runtime.Execution;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.Files;

/// <summary>
/// The bare <c>Print</c> statement - <c>Print "x"</c>, with no file number - which writes to the session's own output by the same
/// rules (<strong>MS-VBAL §5.4.5.8</strong>) as <c>Debug.Print</c> does.
/// </summary>
/// <remarks>
/// In VB6 it is the <c>Print</c> member of the form or report it is written in. Nothing here is one, so it is what an interactive shell
/// - which has no form - writes its output with, and what a <c>BASIC</c> program is written with.
/// </remarks>
[TestClass]
public sealed class BarePrintStatementTests
{
    private static string[] Run(params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [], output, standardLibrary: true, body);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Description);
        return [.. output.Lines];
    }

    [TestMethod]
    public void ABarePrint_WritesToTheSessionsOutput()
        => CollectionAssert.AreEqual(new[] { "hello" }, Run("Print \"hello\""));

    [TestMethod]
    public void ABarePrintWithNoOutputList_WritesABlankLine()
        => CollectionAssert.AreEqual(new[] { "" }, Run("Print"));

    [TestMethod]
    [DataRow("\"a\", \"b\"")]
    [DataRow("\"a\"; \"b\"")]
    [DataRow("1; 2; 3")]
    [DataRow("-5, 7")]
    [DataRow("Spc(3); \"x\"")]
    [DataRow("Tab(10); \"x\"")]
    [DataRow("\"open\";")]
    [DataRow("True, False")]
    [DataRow("1.5")]
    public void ABarePrint_FollowsTheSameOutputRulesAsDebugPrint(string outputList)
        => CollectionAssert.AreEqual(Run($"Debug.Print {outputList}", "Debug.Print"), Run($"Print {outputList}", "Print"));

    [TestMethod]
    public void ABarePrintAndADebugPrint_ShareOneLine_WhenTheFirstIsLeftOpen()
        => CollectionAssert.AreEqual(new[] { "ab" }, Run("Print \"a\";", "Debug.Print \"b\""));

    [TestMethod]
    public void ABarePrint_IsKeywordCaseInsensitive()
        => CollectionAssert.AreEqual(new[] { "x" }, Run("PRINT \"x\""));
}
