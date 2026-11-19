using RDCore.Runtime.Execution;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Runtime.CompilerServices;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program that waits at a <c>Stop</c> under a debugger is ended as the session it runs in is let go of: its thread runs to its end, and nothing holds
/// what ran it.
/// </summary>
[TestClass]
public sealed class SuspendedProgramDisposalTests
{
    private static readonly string Waiting = string.Join("\r\n",
        "Public Sub Main()",
        "    Stop",
        "    Debug.Print \"resumed\"",
        "End Sub") + "\r\n";

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AProgramThatWaitsAtAStop_IsEnded_WhenItsSessionIsLetGoOf(VBExecutionEngine engine)
    {
        ProgramExecution? execution = null;
        IRuntimeSession? session = null;
        WeakReference? gate = null;
        await ModuleWorkspace.InspectAsync([], Waiting, (provider, first, _) =>
        {
            Assert.AreEqual((ExecutionOutcome.Suspended, 1), (first.Outcome, first.ErrorLine), first.ErrorMessage);
            (execution, session) = (provider.Execution, provider.Session);
            gate = WeakGate(session);
            return Task.CompletedTask;
        }, debug: true, engine: engine);

        // the test's inspection is over, and the session provider disposed: the program was ended as it was, and its thread ran out of the entry point.
        Assert.AreEqual(ProgramState.Idle, execution!.State, "the program is over");
        Assert.IsNull(session!.Halt.Gate, "the program's thread ran to its end");
        Assert.AreEqual(0, session.CallStack.Depth, "the program's activations were let go of");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.IsFalse(gate!.IsAlive, "what ran the program is held by nothing");
    }

    // the execution that holds the program at its stop, held weakly: a local of this frame only, which is over once it returns.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WeakGate(IRuntimeSession session)
    {
        Assert.IsInstanceOfType<SuspendableExecution>(session.Halt.Gate);
        return new WeakReference(session.Halt.Gate);
    }
}
