using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// A handler of an event of the session that stops while no program runs (a click on a form a statement showed) waits there when somebody takes the stop
/// (<see cref="IEnvironmentSessionProvider.SessionStopped"/>), and is then the program that waits: it is resumed, stepped and ended as one is.
/// </summary>
[TestClass]
public sealed class SessionEventStopTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static readonly string Handler = string.Join("\r\n",
        "Public Total As Long",
        "Public Sub Clicked()",
        "    Total = 1",
        "    Stop",
        "    Total = Total + 10",
        "    Total = Total + 100",
        "End Sub",
        "Public Sub Main()",
        "End Sub") + "\r\n";

    // the handler, run as an event of the session would be: on a thread of its own, in the session's turn, with no program running.
    private static Task RaiseAsync(EnvironmentSessionProvider provider)
    {
        var session = provider.Session;
        var pipeline = RuntimeExecutionPipeline.Create(session, provider.Image, Substitute.For<IErrorDetailsRecorder>(), CancellationToken.None);
        Assert.IsTrue(session.Symbols.TryResolveValue("Program", GlobalSymbols.UnresolvedSymbol, out var module));
        Assert.IsTrue(session.Symbols.TryResolveValue("Clicked", module!, out var clicked));
        var handler = (VBTypeMemberSymbol)clicked!;
        return Task.Factory.StartNew(
            () => session.Turn.RunEvent(() => pipeline.Invoker.Invoke(handler, session.Symbols.Resolver, [])),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static long Total(IRuntimeSession session)
    {
        Assert.IsTrue(session.Symbols.TryResolveValue("Program", GlobalSymbols.UnresolvedSymbol, out var module));
        Assert.IsTrue(session.Symbols.TryResolveValue("Total", module!, out var total));
        return Convert.ToInt64(session.Symbols.Resolver.GetValue(total!).Value.BoxedValue);
    }

    private static async Task WithSessionAsync(Func<EnvironmentSessionProvider, Task> test)
        => await ModuleWorkspace.InspectAsync([], Handler, async (provider, _, _) =>
        {
            try
            {
                await test(provider);
            }
            finally
            {
                provider.SessionStopped = null;
            }
        });

    // whoever takes the stops, which it says it does, and each stop it was told of.
    private static TaskCompletionSource<ExecuteResult> Takes(EnvironmentSessionProvider provider)
    {
        var told = new TaskCompletionSource<ExecuteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.SessionStopped = stop =>
        {
            told.TrySetResult(stop);
            return Task.FromResult(true);
        };

        return told;
    }

    [TestMethod]
    public async Task AHandlerThatStops_WhenTheStopIsTaken_WaitsThere_AndGoesOnWhenResumed()
        => await WithSessionAsync(async provider =>
        {
            var told = Takes(provider);
            var handled = RaiseAsync(provider);

            var stop = await told.Task.WaitAsync(Patience);
            Assert.AreEqual(ExecutionOutcome.Suspended, stop.Outcome);
            Assert.AreEqual(3, stop.ErrorLine, "the Stop");
            Assert.AreEqual("Program", stop.ErrorModule);
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
            Assert.IsFalse(handled.IsCompleted, "the handler waits");
            Assert.AreEqual(1, Total(provider.Session));

            var resumed = await provider.Execution.ResumeAsync(step: null, CancellationToken.None).WaitAsync(Patience);

            Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome);
            await handled.WaitAsync(Patience);
            Assert.AreEqual(111, Total(provider.Session));
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
        });

    [TestMethod]
    public async Task AHandlerThatStops_IsSteppedAsAProgramIs()
        => await WithSessionAsync(async provider =>
        {
            var told = Takes(provider);
            var handled = RaiseAsync(provider);
            _ = await told.Task.WaitAsync(Patience);

            var stepped = await provider.Execution.ResumeAsync(StepKind.Over, CancellationToken.None).WaitAsync(Patience);

            Assert.AreEqual(ExecutionOutcome.Suspended, stepped.Outcome);
            Assert.AreEqual(4, stepped.ErrorLine, "the statement after the Stop");
            Assert.AreEqual(1, Total(provider.Session), "the step stops before the statement");

            stepped = await provider.Execution.ResumeAsync(StepKind.Over, CancellationToken.None).WaitAsync(Patience);
            Assert.AreEqual(5, stepped.ErrorLine);
            Assert.AreEqual(11, Total(provider.Session));

            Assert.AreEqual(ExecutionOutcome.Completed, (await provider.Execution.ResumeAsync(step: null, CancellationToken.None).WaitAsync(Patience)).Outcome);
            await handled.WaitAsync(Patience);
            Assert.AreEqual(111, Total(provider.Session));
        });

    [TestMethod]
    public async Task AHandlerThatStops_IsEndedByTerminate_AndGoesNoFurther()
        => await WithSessionAsync(async provider =>
        {
            var told = Takes(provider);
            var handled = RaiseAsync(provider);
            _ = await told.Task.WaitAsync(Patience);

            var ended = await provider.Execution.TerminateAsync(wipe: false).WaitAsync(Patience);

            Assert.IsTrue(ended.Acted);
            await handled.WaitAsync(Patience);
            Assert.AreEqual(1, Total(provider.Session));
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
        });

    [TestMethod]
    public async Task AHandlerThatStops_WhenNobodyTakesTheStop_IsStopped()
        => await WithSessionAsync(async provider =>
        {
            var asked = 0;
            provider.SessionStopped = _ =>
            {
                Interlocked.Increment(ref asked);
                return Task.FromResult(false);
            };

            await RaiseAsync(provider).WaitAsync(Patience);

            Assert.AreEqual(1, asked);
            Assert.AreEqual(1, Total(provider.Session), "the Stop stops the handler");
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
        });

    [TestMethod]
    public async Task AHandlerThatStops_WithNobodyToTell_IsStopped_AsItWasBefore()
        => await WithSessionAsync(async provider =>
        {
            provider.SessionStopped = null;

            Assert.IsNull(provider.Session.Halt.Gate, "nothing holds a handler that stops");
            await RaiseAsync(provider).WaitAsync(Patience);

            Assert.AreEqual(1, Total(provider.Session));
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
        });

    [TestMethod]
    public async Task AHandlerThatStopsAgain_AfterTheFirstWasTaken_IsTakenByTheEventHandledAfterIt()
        => await WithSessionAsync(async provider =>
        {
            var told = Takes(provider);
            var first = RaiseAsync(provider);
            _ = await told.Task.WaitAsync(Patience);
            Assert.AreEqual(ExecutionOutcome.Completed, (await provider.Execution.ResumeAsync(step: null, CancellationToken.None).WaitAsync(Patience)).Outcome);
            await first.WaitAsync(Patience);

            told = Takes(provider);
            var second = RaiseAsync(provider);
            var stop = await told.Task.WaitAsync(Patience);

            Assert.AreEqual(ExecutionOutcome.Suspended, stop.Outcome);
            Assert.AreEqual(1, Total(provider.Session), "the second click set it again");
            Assert.AreEqual(ExecutionOutcome.Completed, (await provider.Execution.ResumeAsync(step: null, CancellationToken.None).WaitAsync(Patience)).Outcome);
            await second.WaitAsync(Patience);
        });
}
