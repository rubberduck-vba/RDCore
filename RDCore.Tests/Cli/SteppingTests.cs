using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Cli;

/// <summary>
/// Where a Step Into stops, line by line, as MS-VBA's F8 stops there: once per statement, the test of a single-line <c>If</c> being one of a line's
/// statements, and a post-test loop's <c>Do</c> once, on entry. Whichever engine runs the program.
/// </summary>
[TestClass]
public sealed class SteppingTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private static Task<ExecuteResult> StepInto(EnvironmentSessionProvider provider)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams { Step = StepKind.Into }, CancellationToken.None).WaitAsync(Patience);

    // the block is written at line 3, after the Stop; "steps" is each zero-based line a Step Into stops on, in turn, until the program ends.
    [TestMethod]
    [DataRow("If a = 0 Then a = 1 Else a = 2", "3,3,4,5", DisplayName = "a single-line If, its Then part")]
    [DataRow("If a = 9 Then a = 1 Else a = 2", "3,3,4,5", DisplayName = "a single-line If, its Else part")]
    [DataRow("a = 1: b = 2: i = 3", "3,3,3,4,5", DisplayName = "statements separated by colons")]
    [DataRow("If a = 0 Then a = 1: b = 2 Else a = 3: b = 4", "3,3,3,4,5", DisplayName = "a single-line If, two statements in its Then part")]
    [DataRow("If a = 9 Then a = 1: b = 2 Else a = 3: b = 4", "3,3,3,4,5", DisplayName = "a single-line If, two statements in its Else part")]
    [DataRow("Do|    i = i + 1|Loop While i < 3", "3,4,5,4,5,4,5,6,7", DisplayName = "Do ... Loop While: Do on entry only")]
    [DataRow("Do|    i = i + 1|Loop Until i >= 3", "3,4,5,4,5,4,5,6,7", DisplayName = "Do ... Loop Until: Do on entry only")]
    [DataRow("Do While i < 3|    i = i + 1|Loop", "3,4,5,3,4,5,3,4,5,3,6,7", DisplayName = "Do While ... Loop: Do While on each test")]
    public async Task AStepInto_StopsAsMSVBAStops(string block, string steps)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            await ModuleWorkspace.InspectAsync([], Program(block), async (provider, first, _) =>
            {
                Assert.AreEqual((ExecutionOutcome.Suspended, 2), (first.Outcome, first.ErrorLine), $"{engine}: {first.ErrorMessage}");
                Assert.AreEqual(steps, await StepToTheEnd(provider), engine.ToString());
            }, debug: true, engine: engine);
        }
    }

    // a divergence from MS-VBA, which goes from an Exit Sub straight to the caller: RD-VBA stops at the End Sub on the way, where a breakpoint is.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "interpreted")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "compiled")]
    public async Task AStepInto_AtAnExitSub_StopsAtEndSub(VBExecutionEngine engine)
        => await ModuleWorkspace.InspectAsync([], Program("If a = 0 Then Exit Sub"), async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            Assert.AreEqual("3,3,5", await StepToTheEnd(provider));
        }, debug: true, engine: engine);

    private static string Program(string block) => string.Join("\r\n", [
        "Public Sub Main()",
        "    Dim a As Long, b As Long, i As Long",
        "    Stop",
        .. block.Split('|').Select(line => "    " + line),
        "    a = 0",
        "End Sub"]) + "\r\n";

    private static async Task<string> StepToTheEnd(EnvironmentSessionProvider provider)
    {
        var lines = new List<int>();
        for (var step = 0; step < 30; step++)
        {
            var stop = await StepInto(provider);
            if (stop.Outcome is ExecutionOutcome.Completed)
            {
                return string.Join(",", lines);
            }

            Assert.AreEqual(ExecutionOutcome.Suspended, stop.Outcome, stop.ErrorMessage);
            lines.Add(stop.ErrorLine);
        }

        Assert.Fail($"still stepping after {string.Join(",", lines)}");
        return string.Empty;
    }
}
