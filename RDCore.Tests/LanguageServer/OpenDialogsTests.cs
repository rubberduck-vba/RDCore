using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.LanguageServer.Runtime;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class OpenDialogsTests
{
    // a cap on a hang, not a budget: each wait is on the answer itself, which a loaded machine gives as late as it does.
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(5);

    [TestMethod]
    public async Task APullWhoseCancellationThrows_IsDeclined_AndOnceTheDialogIsAnswered_TheClientIsAskedAgain_AndTheNextPullIsAnswered()
    {
        var pulledAgain = 0;
        var dialogs = new OpenDialogs { PullAgain = () => pulledAgain++ };

        // a pull that is running, and something registered on its token that throws as it is cancelled.
        var host = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pull = dialogs.UnlessADialogIsOpenAsync(CancellationToken.None, token =>
        {
            token.Register(() => throw new InvalidOperationException("the connection is gone"));
            return host.Task;
        });

        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = dialogs.WhileOpenAsync(() => answer.Task);

        var declined = await Assert.ThrowsExactlyAsync<RpcErrorException>(() => pull.WaitAsync(HangGuard));
        Assert.AreEqual(ErrorCodes.ServerCancelled, declined.Code);
        Assert.AreEqual(0, pulledAgain, "not while the dialog is open");

        answer.SetResult("OK");
        Assert.AreEqual("OK", await dialog.WaitAsync(HangGuard), "the program gets its answer, not what the pull's token threw");
        Assert.AreEqual(1, pulledAgain);

        Assert.AreEqual(42, await dialogs.UnlessADialogIsOpenAsync(CancellationToken.None, _ => Task.FromResult(42)).WaitAsync(HangGuard),
            "no dialog is counted open any more");
    }
}
