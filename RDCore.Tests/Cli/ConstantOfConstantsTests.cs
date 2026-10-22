using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A constant whose value names other constants (<strong>MS-VBAL §5.6.16.1</strong>) is evaluated once, from theirs - in a module, in a procedure, and
/// whatever order they are declared in.
/// </summary>
/// <remarks>
/// The value of a constant reaches the host as an expression, over the wire: a request the transport could not write was never sent, and the shell waited
/// for its answer (<c>RUN</c> and <c>ANALYZE</c> froze on any <c>Const</c>).
/// </remarks>
[TestClass]
public sealed class ConstantOfConstantsTests
{
    [TestMethod]
    [Timeout(20000, CooperativeCancellation = true)]
    public async Task AModuleConstantOfModuleConstants_IsTheirValue()
        => CollectionAssert.AreEqual(new[] { "15" }, await RunAsync([], string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Const FIZZ = 3",
            "Const BUZZ = 5",
            "Const FIZZBUZZ = FIZZ * BUZZ",
            "Public Sub Main()",
            "    Debug.Print FIZZBUZZ",
            "End Sub") + "\r\n"));

    // the shell's program is the body of a procedure: its constants are the procedure's.
    [TestMethod]
    [Timeout(20000, CooperativeCancellation = true)]
    public async Task AProcedureConstantOfProcedureConstants_IsTheirValue_InTheShell()
    {
        var result = await ShellHost.Compose().RunAsync([(10, "CONST FIZZ = 3"), (20, "CONST BUZZ = 5"), (30, "CONST FIZZBUZZ = FIZZ * BUZZ"), (40, "PRINT FIZZBUZZ")]);

        Assert.AreEqual("15", string.Concat(result.Output).Trim(), $"{result.Outcome}: {result.ErrorNumber} {result.ErrorMessage} {string.Join(" | ", result.Diagnostics)}");
    }
}
