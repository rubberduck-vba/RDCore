using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host.Handlers;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// A reset between two runs of a program (<see cref="ProgramExecution.ResetAsync"/>): the objects the module variables hold run their
/// <c>Class_Terminate</c>, and the session is then wiped, so that the next run runs as it would in a new session of the program.
/// </summary>
[TestClass]
public sealed class SessionResetTests
{
    private static readonly (string Name, string Source)[] NoClasses = [];

    // a cap on a hang, not a budget: each wait is on the event itself.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private static readonly (string Name, string Source)[] Tracked =
    [
        ("Tracked", ModuleWorkspace.ClassModule("Tracked",
            "Public Id As String",
            "Private Sub Class_Terminate()",
            "Debug.Print \"terminated \" & Id",
            "End Sub")),
    ];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static string[] Lines(ExecuteResult result) => [.. result.Output.Select(line => line.Trim())];

    private static Task<ExecuteResult> Reset(EnvironmentSessionProvider provider) => provider.Execution.ResetAsync().WaitAsync(Patience);

    // everything a run leaves in the session that the next run reads: a module variable, a Static, an object, an open file, Err, and the random numbers.
    private static readonly string Leaving = Program(
        "Private Count As Long",
        "Private Held As Tracked",
        "Public Sub Main()",
        "Static Calls As Long",
        "Count = Count + 1",
        "Calls = Calls + 1",
        "Debug.Print \"count \" & Count & \" calls \" & Calls",
        "Debug.Print \"file \" & FreeFile",
        "Debug.Print \"held \" & (Held Is Nothing)",
        "Debug.Print \"err \" & Err.Number",
        "Debug.Print \"rnd \" & Rnd",
        "Set Held = New Tracked",
        "Held.Id = \"held\"",
        "On Error Resume Next",
        "Open \"kept.txt\" For Output As #FreeFile",
        "Err.Raise 1000",
        "End Sub");

    [TestMethod]
    public async Task ARun_AfterAReset_PrintsWhatTheFirstRunPrinted()
        => await ModuleWorkspace.InspectAsync(Tracked, Leaving, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, first.Outcome, first.ErrorMessage);
            var second = await runAgain().WaitAsync(Patience);
            CollectionAssert.AreNotEqual(Lines(first), Lines(second), "a run that follows another with no reset reads what the first left");

            var reset = await Reset(provider);
            var third = await runAgain().WaitAsync(Patience);

            Assert.AreEqual(ExecutionOutcome.Completed, reset.Outcome, reset.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "terminated held" }, Lines(reset));
            CollectionAssert.AreEqual(Lines(first), Lines(third), string.Join("|", Lines(third)));
        });

    [TestMethod]
    public async Task AReset_RunsTheTerminateOfEachObjectTheModulesHold_Once()
        => await ModuleWorkspace.InspectAsync(Tracked, Program(
            "Public Held As Tracked",
            "Public Again As Tracked",
            "Public Kept As Variant",
            "Public Several(1 To 2) As Tracked",
            "Public Sub Main()",
            "Set Held = Made(\"held twice\")",
            "Set Again = Held",
            "Set Kept = New Collection",
            "Kept.Add Made(\"in a collection\")",
            "Set Several(2) = Made(\"in an array\")",
            "Remember",
            "End Sub",
            "Private Sub Remember()",
            "Static Last As Tracked",
            "Set Last = Made(\"static\")",
            "End Sub",
            "Private Function Made(ByVal Id As String) As Tracked",
            "Set Made = New Tracked",
            "Made.Id = Id",
            "End Function"), async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, first.Outcome, first.ErrorMessage);
            Assert.AreEqual(0, first.Output.Count, string.Join("|", Lines(first)));

            var reset = await Reset(provider);
            var again = await Reset(provider);

            Assert.AreEqual(ExecutionOutcome.Completed, reset.Outcome, reset.ErrorMessage);
            CollectionAssert.AreEquivalent(
                new[] { "terminated held twice", "terminated in a collection", "terminated in an array", "terminated static" }, Lines(reset));
            Assert.AreEqual(ExecutionOutcome.Completed, again.Outcome, again.ErrorMessage);
            Assert.AreEqual(0, again.Output.Count, "nothing is left to terminate");
        });

    // two objects whose Terminate fails: the first that runs ends the release, and the wipe lets go of the other without running its Terminate.
    [TestMethod]
    public async Task ATerminate_ThatLeavesAnErrorUnhandled_FailsTheReset_AndTheNextRunIsAsNew()
        => await ModuleWorkspace.InspectAsync(
            [("Failing", ModuleWorkspace.ClassModule("Failing",
                "Private Sub Class_Terminate()",
                "Debug.Print \"terminating\"",
                "Err.Raise 1234",
                "End Sub"))],
            Program(
                "Public One As Failing",
                "Public Other As Failing",
                "Public Count As Long",
                "Public Sub Main()",
                "Count = Count + 1",
                "Debug.Print Count",
                "Set One = New Failing",
                "Set Other = New Failing",
                "End Sub"), async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, first.Outcome, first.ErrorMessage);

            var reset = await Reset(provider);
            var next = await runAgain().WaitAsync(Patience);

            Assert.AreEqual(ExecutionOutcome.RuntimeError, reset.Outcome, reset.ErrorMessage);
            Assert.AreEqual(1234, reset.ErrorNumber);
            Assert.AreEqual("Failing", reset.ErrorModule, "where it was raised");
            CollectionAssert.AreEqual(new[] { "terminating" }, Lines(reset));
            Assert.AreEqual(ExecutionOutcome.Completed, next.Outcome, next.ErrorMessage);
            CollectionAssert.AreEqual(Lines(first), Lines(next));
        });

    [TestMethod]
    public async Task AReset_OfAProgramThatWaits_EndsIt_AndTheNextRunIsAsNew()
        => await ModuleWorkspace.InspectAsync(NoClasses, Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print Total",
            "    Total = Total + 1",
            "    Stop",
            "End Sub"), async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);

            var reset = await Reset(provider);

            Assert.AreEqual(ExecutionOutcome.Completed, reset.Outcome, reset.ErrorMessage);
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
            Assert.AreEqual(0, provider.Session.CallStack.Depth);

            var again = await runAgain().WaitAsync(Patience);
            CollectionAssert.AreEqual(new[] { "0" }, Lines(again));
        }, debug: true);

    [TestMethod]
    public async Task AReset_WhileAProgramRuns_IsRefused()
        => await ModuleWorkspace.InspectAsync(NoClasses, Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Do While True",
            "        Total = Total + 1",
            "    Loop",
            "End Sub"), async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, "the cancellation of the request is a break");

            var running = new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                .Handle(new HostDebugResumeParams(), CancellationToken.None);
            await ModuleWorkspace.WhenRunning(provider).WaitAsync(Patience);

            var refused = await Reset(provider);

            Assert.AreEqual(ExecutionOutcome.Refused, refused.Outcome);
            var ended = await new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);
            Assert.IsTrue(ended.Acted);
            Assert.AreEqual(ExecutionOutcome.Halted, (await running.WaitAsync(Patience)).Outcome);
        }, cancelWhen: ModuleWorkspace.WhenRunning, debug: true);
}
