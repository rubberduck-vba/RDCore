using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RDCore.External.Forms;
using RDCore.External.Forms.RDForms;
using RDCore.External.Interaction;
using RDCore.Forms.Avalonia.Dialogs;
using RDCore.SDK.Platform;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.Tests.External;

/// <summary>
/// RDForms' dialogs of a program whose forms RDForms hosts, in headless windows: a <c>MsgBox</c> and an <c>InputBox</c> answered by the button that closes
/// them, shown over the active form while the others wait, in the forms' theme, opening the help topic the program names, and closed when the program that
/// waits for them is stopped.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class RDFormsUserInteractionTests
{
    private sealed class Help : IHelpViewer
    {
        public List<(string File, int? Context)> Opened { get; } = [];

        public void Open(string file, int? context) => Opened.Add((file, context));
    }

    private static RDFormsFormHost Host() => new(new RDFormsAutomationServer(RDFormsPlatform.Headless));

    // a prompt no other test's dialog shows: what tells a test's dialog from the others that open meanwhile.
    private static string Prompt() => $"Sure? {Guid.NewGuid():N}";

    // what is done with the dialog showing prompt, once it is open: on the UI thread, after the call that opened it. The handler is added and removed on
    // that thread, which every window's events are raised on.
    private static Opened WhenOpen<TDialog>(string prompt, Action<TDialog> answer) where TDialog : Window
    {
        RDFormsUI.Start(RDFormsPlatform.Headless);
        return new Opened(RDFormsUI.Run(() => Window.WindowOpenedEvent.AddClassHandler<TDialog>((dialog, _) =>
        {
            if (PromptOf(dialog) == prompt)
            {
                Dispatcher.UIThread.Post(() => answer(dialog));
            }
        })));
    }

    private sealed class Opened(IDisposable handler) : IDisposable
    {
        public void Dispose() => RDFormsUI.Run(handler.Dispose);
    }

    private static string? PromptOf(Window dialog) => dialog switch
    {
        RDFormsMessageBox message => message.Prompt,
        RDFormsInputBox input => input.Prompt,
        _ => null,
    };

    private static FormDefinition VisualBasicForm(string name)
        => new(name, ReadOnlyMemory<byte>.Empty) { Kind = FormDesignerKind.VisualBasic6, Caption = name };

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [TestMethod]
    public void AMessageBox_AnswersTheButtonThatClosedIt()
    {
        var prompt = Prompt();
        using var opened = WhenOpen<RDFormsMessageBox>(prompt, dialog => dialog.Choose(VBMsgBoxResult.VBNo));

        var answer = new RDFormsUserInteraction(Host()).ShowMessage(new MessageBoxRequest(prompt, "Confirm", VBMsgBoxStyle.VBYesNo));

        Assert.AreEqual(VBMsgBoxResult.VBNo, answer);
    }

    [TestMethod]
    public void AnInputBox_AnswersWhatWasTyped_OrNothingWhenItIsCancelled()
    {
        var typed = Prompt();
        var cancelled = Prompt();
        using var typing = WhenOpen<RDFormsInputBox>(typed, dialog =>
        {
            dialog.Input.Text = "Ducky";
            dialog.Confirm();
        });
        using var cancelling = WhenOpen<RDFormsInputBox>(cancelled, dialog => dialog.Cancel());
        var dialogs = new RDFormsUserInteraction(Host());

        Assert.AreEqual("Ducky", dialogs.Prompt(new InputBoxRequest(typed, "Who", "you")));
        Assert.IsNull(dialogs.Prompt(new InputBoxRequest(cancelled, "Who", "you")));
    }

    [TestMethod]
    [DataRow(true, DisplayName = "the system's theme")]
    [DataRow(false, DisplayName = "the light palette")]
    public void ADialog_IsDrawnInTheThemeOfTheForms(bool keepSystemTheme)
    {
        var prompt = Prompt();
        bool? drawn = null;
        using var opened = WhenOpen<RDFormsMessageBox>(prompt, dialog =>
        {
            drawn = dialog.KeepSystemTheme;
            dialog.Choose(VBMsgBoxResult.VBOK);
        });
        var host = Host();
        host.KeepSystemTheme = keepSystemTheme;

        _ = new RDFormsUserInteraction(host).ShowMessage(new MessageBoxRequest(prompt, "T", VBMsgBoxStyle.VBOKOnly));

        Assert.AreEqual(keepSystemTheme, drawn);
    }

    // the Help button opens the topic the program names, and leaves the dialog open; with no help file named, it opens nothing.
    [TestMethod]
    public void TheHelpButton_OpensTheTopicTheProgramNames_AndLeavesTheDialogOpen()
    {
        var named = Prompt();
        var unnamed = Prompt();
        bool? openAfterHelp = null;
        using var helping = WhenOpen<RDFormsMessageBox>(named, dialog =>
        {
            Click(dialog.HelpButton!);
            openAfterHelp = dialog.IsVisible;
            dialog.Choose(VBMsgBoxResult.VBOK);
        });
        using var helpingInput = WhenOpen<RDFormsInputBox>(named + " input", dialog =>
        {
            Click(dialog.HelpButton!);
            dialog.Confirm();
        });
        using var nothing = WhenOpen<RDFormsMessageBox>(unnamed, dialog =>
        {
            Click(dialog.HelpButton!);
            dialog.Choose(VBMsgBoxResult.VBOK);
        });
        var help = new Help();
        var dialogs = new RDFormsUserInteraction(Host(), help);

        _ = dialogs.ShowMessage(new MessageBoxRequest(named, "T", VBMsgBoxStyle.VBOKOnly | VBMsgBoxStyle.VBMsgBoxHelpButton, "app.chm", 42));
        _ = dialogs.Prompt(new InputBoxRequest(named + " input", "T", HelpFile: "app.chm", Context: 7));
        _ = dialogs.ShowMessage(new MessageBoxRequest(unnamed, "T", VBMsgBoxStyle.VBOKOnly | VBMsgBoxStyle.VBMsgBoxHelpButton));

        Assert.IsTrue(openAfterHelp);
        CollectionAssert.AreEqual(new[] { ("app.chm", (int?)42), ("app.chm", (int?)7) }, help.Opened);
    }

    // a dialog Esc does not close is closed with its default button, and one Esc does close as Esc closes it; the call is cancelled either way.
    [TestMethod]
    [DataRow(VBMsgBoxStyle.VBYesNo, DisplayName = "a dialog Esc does not close")]
    [DataRow(VBMsgBoxStyle.VBOKCancel, DisplayName = "a dialog Esc closes")]
    public void ADialogOpenWhenItsProgramIsStopped_IsClosed(VBMsgBoxStyle style)
    {
        var prompt = Prompt();
        using var stop = new CancellationTokenSource();
        RDFormsMessageBox? shown = null;
        using var opened = WhenOpen<RDFormsMessageBox>(prompt, dialog =>
        {
            shown = dialog;
            stop.Cancel();
        });

        Assert.ThrowsExactly<OperationCanceledException>(
            () => new RDFormsUserInteraction(Host()).ShowMessage(new MessageBoxRequest(prompt, "T", style), stop.Token));
        Assert.IsFalse(RDFormsUI.Run(() => shown!.IsVisible));
    }

    [TestMethod]
    public void AnInputBoxOpenWhenItsProgramIsStopped_IsCancelled()
    {
        var prompt = Prompt();
        using var stop = new CancellationTokenSource();
        RDFormsInputBox? shown = null;
        using var opened = WhenOpen<RDFormsInputBox>(prompt, dialog =>
        {
            shown = dialog;
            stop.Cancel();
        });

        Assert.ThrowsExactly<OperationCanceledException>(() => new RDFormsUserInteraction(Host()).Prompt(new InputBoxRequest(prompt, "T"), stop.Token));
        Assert.IsFalse(RDFormsUI.Run(() => shown!.IsVisible));
        Assert.IsNull(shown!.Result);
    }

    [TestMethod]
    public void ADialogOfAProgramStoppedAlready_IsNotShown()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var dialogs = new RDFormsUserInteraction(Host());

        Assert.ThrowsExactly<OperationCanceledException>(() => dialogs.ShowMessage(new MessageBoxRequest(Prompt(), "T", VBMsgBoxStyle.VBOKOnly), stop.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => dialogs.Prompt(new InputBoxRequest(Prompt(), "T"), stop.Token));
    }

    // the active form owns the dialog, and the other forms wait until it is closed, as they do for a modal form. The activation is the UI thread's, which
    // every RDForms window shares: the test runs alone.
    [TestMethod]
    [DoNotParallelize]
    public void ADialog_IsShownOverTheActiveForm_WhileTheOtherFormsWait()
    {
        var host = Host();
        var active = host.Create(VisualBasicForm("Active"));
        var other = host.Create(VisualBasicForm("Other"));
        try
        {
            host.Show(other, modal: false);
            host.Show(active, modal: false);
            var owning = RDFormsAutomationServer.HandleOf(active).Form.Window;
            var waiting = RDFormsAutomationServer.HandleOf(other).Form.Window;
            RDFormsUI.Run(() =>
            {
                owning.Activate();
                Dispatcher.UIThread.RunJobs();
            });

            var prompt = Prompt();
            (object? Owner, bool OtherEnabled)? seen = null;
            using var opened = WhenOpen<RDFormsMessageBox>(prompt, dialog =>
            {
                seen = (dialog.Owner, waiting.IsEnabled);
                dialog.Choose(VBMsgBoxResult.VBOK);
            });

            _ = new RDFormsUserInteraction(host).ShowMessage(new MessageBoxRequest(prompt, "T", VBMsgBoxStyle.VBOKOnly));

            Assert.AreSame(owning, seen?.Owner, "the active form owns the dialog");
            Assert.IsFalse(seen?.OtherEnabled, "the other form waits while the dialog is open");
            Assert.IsTrue(RDFormsUI.Run(() => waiting.IsEnabled), "the other form no longer waits once the dialog is closed");
        }
        finally
        {
            host.Destroy(active);
            host.Destroy(other);
        }
    }
}
