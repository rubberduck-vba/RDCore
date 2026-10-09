using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program run under a debugger can have what it prints said as it prints, and not with the answer to the request that runs it: a debugger shows the output of a
/// program that has not finished.
/// </summary>
[TestClass]
public sealed class StreamedOutputTests
{
    private static readonly (int Number, string Statement)[] Program =
        [(10, "PRINT \"a\""), (20, "PRINT \"b\""), (30, "STOP"), (40, "PRINT \"c\""), (50, "PRINT \"d\";")];

    private static string[] Trimmed(IEnumerable<string> lines) => [.. lines.Select(line => line.Trim())];

    private static (List<string> Lines, List<long> Totals) Listen(ShellHost shell)
    {
        var lines = new List<string>();
        var totals = new List<long>();
        shell.Provider.OutputStreamed = (said, total) =>
        {
            lines.AddRange(said);
            totals.Add(total);
        };

        return (lines, totals);
    }

    [TestMethod]
    public async Task AProgramThatStreams_SaysItsLinesAsItPrints_AndTheAnswerHasNoneOfThem()
    {
        var shell = ShellHost.Compose();
        var (said, totals) = Listen(shell);

        var stopped = await shell.RunAsync(Program, debug: true, streamOutput: true);

        Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "a", "b" }, Trimmed(said));
        CollectionAssert.AreEqual(new long[] { 1, 2 }, totals);
        Assert.AreEqual(0, stopped.Output.Count, "what was said is not said again");
        Assert.AreEqual(2, stopped.StreamedLines);
    }

    [TestMethod]
    public async Task AProgramThatStreams_KeepsStreamingWhenItIsResumed_AndTheLineItLeftOpenIsInTheAnswer()
    {
        var shell = ShellHost.Compose();
        var (said, _) = Listen(shell);
        _ = await shell.RunAsync(Program, debug: true, streamOutput: true);

        var resumed = await new HostDebugResumeHandler(shell.Provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams(), CancellationToken.None);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Trimmed(said));
        CollectionAssert.AreEqual(new[] { "d" }, Trimmed(resumed.Output), "a line that was not ended might not be the last, and is not said");
        Assert.AreEqual(3, resumed.StreamedLines);
    }

    [TestMethod]
    public async Task AProgramThatDoesNotAskToStream_HasItsOutputInTheAnswers()
    {
        var shell = ShellHost.Compose();
        var (said, _) = Listen(shell);

        var stopped = await shell.RunAsync(Program, debug: true);

        Assert.AreEqual(0, said.Count);
        CollectionAssert.AreEqual(new[] { "a", "b" }, Trimmed(stopped.Output));
        Assert.AreEqual(0, stopped.StreamedLines);
    }

    [TestMethod]
    public async Task AProgramThatAsksToStream_WhereNobodyListens_HasItsOutputInTheAnswers()
    {
        var shell = ShellHost.Compose();

        var stopped = await shell.RunAsync(Program, debug: true, streamOutput: true);

        CollectionAssert.AreEqual(new[] { "a", "b" }, Trimmed(stopped.Output));
    }

    [TestMethod]
    public async Task AProgramThatIsNotUnderADebugger_DoesNotStream()
    {
        var shell = ShellHost.Compose();
        var (said, _) = Listen(shell);

        var done = await shell.RunAsync([(10, "PRINT \"a\"")], debug: false, streamOutput: true);

        Assert.AreEqual(0, said.Count);
        CollectionAssert.AreEqual(new[] { "a" }, Trimmed(done.Output));
    }
}
