using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>REM</c> line of a BASIC program, run the way the shell runs one: it is a comment, and a comment has nothing to run - but it is a line, and so a
/// place a <c>GOTO</c> can go.
/// </summary>
[TestClass]
public sealed class RemStatementShellTests
{
    private static async Task<string[]> RunAsync(params (int Number, string Statement)[] lines)
    {
        var result = await ShellHost.Compose().RunAsync(lines);

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage + string.Join("; ", result.Diagnostics));
        return [.. result.Output.Select(line => line.Trim())];
    }

    [TestMethod]
    public async Task ARemLine_IsSkipped_WhateverItSays()
    {
        CollectionAssert.AreEqual(new[] { "1" }, await RunAsync((10, "REM hello"), (20, "PRINT 1")));
        CollectionAssert.AreEqual(new[] { "2" }, await RunAsync((10, "Rem hello world"), (20, "PRINT 2")));
        CollectionAssert.AreEqual(new[] { "3" }, await RunAsync((10, "REM"), (20, "PRINT 3")));
    }

    [TestMethod]
    public async Task ARemAfterAStatementAndAColon_LeavesTheStatement()
        => CollectionAssert.AreEqual(new[] { "3", "4" }, await RunAsync((10, "PRINT 3 : REM trailing"), (20, "PRINT 4")));

    [TestMethod]
    public async Task ARemComment_SwallowsAColonAndWhatFollowsIt()
        => CollectionAssert.AreEqual(new[] { "5" }, await RunAsync((10, "REM : PRINT 9"), (20, "PRINT 5")));

    [TestMethod]
    public async Task ARemLine_IsALineAGotoCanGoTo()
        => CollectionAssert.AreEqual(new[] { "9" }, await RunAsync((10, "GOTO 30"), (20, "PRINT 8"), (30, "REM target"), (40, "PRINT 9")));

    [TestMethod]
    public async Task ARemLineInsideABlock_IsSkipped()
        => CollectionAssert.AreEqual(new[] { "7" }, await RunAsync((10, "IF 1 = 1 THEN"), (20, "REM inside"), (30, "PRINT 7"), (40, "END IF")));

    [TestMethod]
    public async Task ARemLineInsideALoop_IsSkippedEveryTime()
        => CollectionAssert.AreEqual(new[] { "1", "2", "3" }, await RunAsync((10, "FOR I = 1 TO 3"), (20, "REM each time"), (30, "PRINT I"), (40, "NEXT")));
}
