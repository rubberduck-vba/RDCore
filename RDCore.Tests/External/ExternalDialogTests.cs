using NSubstitute;
using RDCore.External.Automation;
using RDCore.External.Client;
using RDCore.External.Hosting;
using RDCore.External.Interaction;
using RDCore.External.Protocol;
using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.Tests.External;

/// <summary>
/// The dialogs an external host shows beside its forms: a <c>MsgBox</c> and an <c>InputBox</c> answered once they are closed, closed when the program that
/// waits for them is stopped, and left to the environment host's own dialogs when the external host shows none.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class ExternalDialogTests
{
    // dialogs that answer as they are told, and remember what they were asked.
    private sealed class Dialogs : IUserInteraction
    {
        public Func<MessageBoxRequest, CancellationToken, VBMsgBoxResult> Message { get; init; } = (_, _) => VBMsgBoxResult.VBOK;

        public Func<InputBoxRequest, CancellationToken, string?> Input { get; init; } = (request, _) => request.Default;

        public List<object> Asked { get; } = [];

        public VBMsgBoxResult ShowMessage(MessageBoxRequest request, CancellationToken token = default)
        {
            Asked.Add(request);
            return Message(request, token);
        }

        public string? Prompt(InputBoxRequest request, CancellationToken token = default)
        {
            Asked.Add(request);
            return Input(request, token);
        }
    }

    // a dialog that stays open until its token closes it: shown is set once it is open.
    private static Func<CancellationToken, T> OpenUntilClosed<T>(ManualResetEventSlim shown) => token =>
    {
        shown.Set();
        token.WaitHandle.WaitOne();
        throw new OperationCanceledException(token);
    };

    private static ExternalAutomationService Service(IUserInteraction? dialogs) => new(UnavailableAutomationServer.Instance, dialogs: dialogs);

    [TestMethod]
    public void AnExternalHost_SaysWhetherItShowsDialogs()
    {
        Assert.IsTrue(Service(new Dialogs()).Status(new AutomationStatusParams()).HasDialogs);
        Assert.IsFalse(Service(null).Status(new AutomationStatusParams()).HasDialogs);
    }

    [TestMethod]
    public void AMessageBox_CrossesAsItWasAsked_AndAnswersTheButtonThatClosedIt()
    {
        var dialogs = new Dialogs { Message = (_, _) => VBMsgBoxResult.VBNo };
        var asked = new MessageBoxRequest("Sure?", "Confirm", VBMsgBoxStyle.VBYesNo | VBMsgBoxStyle.VBMsgBoxHelpButton, "app.chm", 42);

        var answer = Service(dialogs).MessageBox(FormMessageBoxParams.Of(asked, dialog: 1, handlingEvent: 0));

        Assert.IsNull(answer.Failure);
        Assert.AreEqual((int)VBMsgBoxResult.VBNo, answer.Result);
        Assert.AreEqual(asked, dialogs.Asked.Single());
    }

    [TestMethod]
    public void AnInputBox_CrossesAsItWasAsked_AndAnswersWhatWasTyped_OrNothingWhenItIsCancelled()
    {
        var typed = new Dialogs { Input = (_, _) => "Ducky" };
        var cancelled = new Dialogs { Input = (_, _) => null };
        var asked = new InputBoxRequest("Name?", "Who", "you", 100, 200, "app.chm", 7);

        Assert.AreEqual("Ducky", Service(typed).InputBox(FormInputBoxParams.Of(asked, dialog: 1, handlingEvent: 0)).Value);
        Assert.AreEqual(asked, typed.Asked.Single());
        Assert.IsNull(Service(cancelled).InputBox(FormInputBoxParams.Of(asked, dialog: 1, handlingEvent: 0)).Value);
    }

    // Esc answers vbCancel where the dialog has a Cancel button; a dialog Esc does not close answers its default button.
    [TestMethod]
    [DataRow(VBMsgBoxStyle.VBYesNoCancel, VBMsgBoxResult.VBCancel, DisplayName = "what Esc answers")]
    [DataRow(VBMsgBoxStyle.VBYesNo | VBMsgBoxStyle.VBDefaultButton2, VBMsgBoxResult.VBNo, DisplayName = "the default button, when Esc does not close it")]
    public async Task AMessageBoxOpenWhenItsProgramIsStopped_IsClosed_AndAnswersWhatItsCloseButtonDoes(VBMsgBoxStyle style, VBMsgBoxResult expected)
    {
        using var shown = new ManualResetEventSlim();
        var service = Service(new Dialogs { Message = (_, token) => OpenUntilClosed<VBMsgBoxResult>(shown)(token) });

        var answering = Task.Run(() => service.MessageBox(FormMessageBoxParams.Of(new MessageBoxRequest("x", "T", style), dialog: 7, handlingEvent: 0)));
        shown.Wait();
        _ = service.CloseDialog(new FormCloseDialogParams { Dialog = 7 });
        var answer = await answering;

        Assert.IsNull(answer.Failure);
        Assert.AreEqual((int)expected, answer.Result);
    }

    [TestMethod]
    public async Task AnInputBoxOpenWhenItsProgramIsStopped_IsClosed_AndAnswersNothing()
    {
        using var shown = new ManualResetEventSlim();
        var service = Service(new Dialogs { Input = (_, token) => OpenUntilClosed<string?>(shown)(token) });

        var answering = Task.Run(() => service.InputBox(FormInputBoxParams.Of(new InputBoxRequest("x", "T", "typed"), dialog: 8, handlingEvent: 0)));
        shown.Wait();
        _ = service.CloseDialog(new FormCloseDialogParams { Dialog = 8 });
        var answer = await answering;

        Assert.IsNull(answer.Failure);
        Assert.IsNull(answer.Value);
    }

    // the close of a program stopped as its dialog was being asked for can come first: the dialog is not shown, and answers what its close button does.
    [TestMethod]
    public void ADialogClosedBeforeItIsShown_IsNotShown()
    {
        var dialogs = new Dialogs();
        var service = Service(dialogs);

        _ = service.CloseDialog(new FormCloseDialogParams { Dialog = 9 });
        var answer = service.MessageBox(FormMessageBoxParams.Of(new MessageBoxRequest("x", "T", VBMsgBoxStyle.VBOKCancel), dialog: 9, handlingEvent: 0));

        Assert.AreEqual((int)VBMsgBoxResult.VBCancel, answer.Result);
        Assert.IsEmpty(dialogs.Asked);
    }

    [TestMethod]
    public void AnExternalHostWithNoDialogs_FailsADialogWithTheCodeThatLeavesItToTheEnvironmentHost()
    {
        var service = Service(null);

        var message = service.MessageBox(FormMessageBoxParams.Of(new MessageBoxRequest("x", "T", VBMsgBoxStyle.VBOKOnly), dialog: 1, handlingEvent: 0));
        var input = service.InputBox(FormInputBoxParams.Of(new InputBoxRequest("x", "T"), dialog: 2, handlingEvent: 0));

        Assert.AreEqual(ExternalProtocol.NoDialogs, message.Failure?.HResult);
        Assert.AreEqual(ExternalProtocol.NoDialogs, input.Failure?.HResult);
    }

    // an external host that cannot be started shows no dialog: the environment host's own answer.
    [TestMethod]
    public void TheDialogsOfAnExternalHostThatCannotBeStarted_AreTheFallbacks()
    {
        using var host = ExternalHosts.New(canStart: () => false);
        var fallback = Substitute.For<IUserInteraction>();
        fallback.ShowMessage(Arg.Any<MessageBoxRequest>(), Arg.Any<CancellationToken>()).Returns(VBMsgBoxResult.VBRetry);
        fallback.Prompt(Arg.Any<InputBoxRequest>(), Arg.Any<CancellationToken>()).Returns("typed");
        var dialogs = new RemoteUserInteraction(new RemoteAutomationServer(host), fallback);

        Assert.AreEqual(VBMsgBoxResult.VBRetry, dialogs.ShowMessage(new MessageBoxRequest("x", "T", VBMsgBoxStyle.VBRetryCancel)));
        Assert.AreEqual("typed", dialogs.Prompt(new InputBoxRequest("x", "T")));
    }
}
