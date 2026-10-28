using RDCore.CLI.Host;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program whose dialog the client answers waits for that answer over the connection its own request came in on - so the request's thread, which is the thread
/// that reads the connection, must not be the one the program runs on. When it was, the answer arrived and was never read: the shell never said READY again, and a
/// break could not stop it either, its <c>$/cancelRequest</c> being unread too.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.8.1.13 MsgBox")]
public sealed class DialogOffTheConnectionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The client's dialogs: each waits until the test answers it, as a person at the shell would.
    /// </summary>
    private sealed class WaitingClient : IHostInteractionChannel
    {
        private readonly TaskCompletionSource<MessageBoxResult> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<HostMessageBoxParams> Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Answer(VBMsgBoxResult result) => _answer.TrySetResult(new MessageBoxResult { Handled = true, Result = (int)result });

        public Task<MessageBoxResult> MessageBoxAsync(HostMessageBoxParams request, CancellationToken token)
        {
            Asked.TrySetResult(request);
            return _answer.Task.WaitAsync(token);
        }

        public Task<InputBoxResult> InputBoxAsync(HostInputBoxParams request, CancellationToken token)
            => Task.FromResult(new InputBoxResult { Handled = true, Value = "typed" });
    }

    // calls the request the way the connection calls a handler - on the thread that reads it - and says whether that thread got its task back while the program runs.
    private static Task<ExecuteSessionResult> Request(Func<Task<ExecuteSessionResult>> request, WaitingClient client)
    {
        Task<ExecuteSessionResult>? running = null;
        var reader = new Thread(() => running = request()) { IsBackground = true, Name = "connection reader" };
        reader.Start();

        var returned = reader.Join(Patience);
        if (!returned)
        {
            // let the program go, so that a failing test does not leave a thread behind.
            client.Answer(VBMsgBoxResult.VBCancel);
        }

        Assert.IsTrue(returned, "the thread of the request is kept for as long as the program runs: a dialog's answer over the same connection would never be read");
        return running!;
    }

    [TestMethod]
    public async Task ALineTypedAtThePrompt_RunsOffTheConnection_AndTheAnswerOfItsDialogGetsThrough()
    {
        var shell = ShellHost.Compose();
        var client = new WaitingClient();
        shell.Provider.Interaction = client;

        var running = Request(() => shell.ImmediateAsync([], "PRINT \"before\": PRINT MSGBOX(\"Continue?\", 4)"), client);

        var asked = await client.Asked.Task.WaitAsync(Patience);
        Assert.IsFalse(running.IsCompleted, "the program waits for the answer");
        CollectionAssert.AreEqual(new[] { "before" }, asked.Output.ToArray(), "what it printed first is shown before the dialog");

        client.Answer(VBMsgBoxResult.VBNo);
        var result = await running.WaitAsync(Patience);

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "7" }, result.Output.Select(line => line.Trim()).ToArray(), "and is not said again with the answer");
    }

    [TestMethod]
    public async Task ALineTypedAtAStop_RunsOffTheConnection_AndTheProgramCanBeAskedAboutMeanwhile()
    {
        (int, string)[] program = [(10, "X = 1"), (20, "STOP"), (30, "PRINT X")];
        var shell = ShellHost.Compose();
        var stopped = await shell.RunAsync(program, debug: true);
        Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);

        var client = new WaitingClient();
        shell.Provider.Interaction = client;
        var running = Request(() => shell.ImmediateAsync(program, "PRINT MSGBOX(\"X is \" & X, 1)", alongside: true), client);
        await client.Asked.Task.WaitAsync(Patience);

        // a request that asks about the program is answered while the dialog waits - it would wait for the statement to finish if it were still run with the lock held.
        var state = Task.Run(() => shell.Provider.Execution.State);
        Assert.AreEqual(ProgramState.Suspended, await state.WaitAsync(Patience));

        // and nothing goes on with the program, or runs beside it, until the statement is done.
        var resumed = await shell.Provider.Execution.ResumeAsync(null, CancellationToken.None).WaitAsync(Patience);
        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome);
        var another = await shell.ImmediateAsync(program, "PRINT 1", alongside: true).WaitAsync(Patience);
        Assert.AreEqual(ExecutionOutcome.Refused, another.Outcome);

        client.Answer(VBMsgBoxResult.VBOK);
        var typed = await running.WaitAsync(Patience);
        Assert.AreEqual(ExecutionOutcome.Completed, typed.Outcome, typed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "1" }, typed.Output.Select(line => line.Trim()).ToArray());

        // the program still waits where it stopped, and goes on once the statement is done.
        var finished = await shell.Provider.Execution.ResumeAsync(null, CancellationToken.None).WaitAsync(Patience);
        Assert.AreEqual(ExecutionOutcome.Completed, finished.Outcome, finished.ErrorMessage);
    }
}
