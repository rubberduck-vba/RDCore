using Avalonia.Controls;
using Avalonia.Threading;
using RDCore.External.Automation;
using RDCore.External.Interaction;
using RDCore.External.Protocol;
using RDCore.Forms.Avalonia.Dialogs;
using RDCore.SDK.Platform;
using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.External.Forms.RDForms;

/// <summary>
/// The dialogs of a program whose forms RDForms hosts: <c>MsgBox</c> (MS-VBAL §6.1.2.8.1.13) and <c>InputBox</c> (MS-VBAL §6.1.2.8.1.12) in RDForms
/// windows, shown over the active form in the theme its form host gives the forms.
/// </summary>
/// <remarks>
/// A dialog waits as a modal form does: the other forms are disabled until it is closed, and what they raise meanwhile is handled as it comes. Its
/// <em>Help</em> button and <kbd>F1</kbd> open the help topic the program names, and leave the dialog open. A dialog open when its token is cancelled is
/// closed as <kbd>Esc</kbd> closes it, or with its default button when <kbd>Esc</kbd> does not, and the call throws
/// <see cref="OperationCanceledException"/>.
/// </remarks>
/// <param name="forms">The form host whose forms the dialogs are shown over.</param>
/// <param name="help">What opens the help topics; <see cref="ShellHelpViewer.Instance"/> when <see langword="null"/>.</param>
public sealed class RDFormsUserInteraction(RDFormsFormHost forms, IHelpViewer? help = null) : IUserInteraction
{
    private readonly IHelpViewer _help = help ?? ShellHelpViewer.Instance;

    /// <inheritdoc/>
    public VBMsgBoxResult ShowMessage(MessageBoxRequest request, CancellationToken token = default)
    {
        var byDefault = VBMsgBoxButtons.EscapeResultOf(request.Style)
            ?? VBMsgBoxButtons.ChoicesOf(request.Style)[VBMsgBoxButtons.DefaultChoiceOf(request.Style)];
        return Show(
            () =>
            {
                var dialog = new RDFormsMessageBox(request.Prompt, request.Title, request.Style) { KeepSystemTheme = forms.KeepSystemTheme };
                dialog.HelpRequested += (_, _) => OpenHelp(request.HelpFile, request.Context);
                return dialog;
            },
            (dialog, owner) => dialog.ShowAsync(owner),
            dialog => dialog.Choose(byDefault),
            token);
    }

    /// <inheritdoc/>
    public string? Prompt(InputBoxRequest request, CancellationToken token = default) => Show(
        () =>
        {
            var dialog = new RDFormsInputBox(request.Prompt, request.Title, request.Default, request.XPos, request.YPos, request.HasHelp)
            {
                KeepSystemTheme = forms.KeepSystemTheme,
            };
            dialog.HelpRequested += (_, _) => OpenHelp(request.HelpFile, request.Context);
            return dialog;
        },
        (dialog, owner) => dialog.ShowAsync(owner),
        dialog => dialog.Cancel(),
        token);

    private void OpenHelp(string? file, int? context)
    {
        if (file is not null)
        {
            _help.Open(file, context);
        }
    }

    // shows a dialog on the UI thread, which is started for it when no form has started it, and waits there for its answer on a frame of the thread's own.
    // The cancellation is thrown once the UI thread's call is answered: one thrown inside it would fail the call instead.
    private T Show<TDialog, T>(Func<TDialog> create, Func<TDialog, Window?, Task<T>> show, Action<TDialog> close, CancellationToken token)
        where TDialog : Window
    {
        token.ThrowIfCancellationRequested();
        if (!RDFormsUI.IsStarted)
        {
            // windows that cannot be started, as on a machine with no display, show no dialog: the environment host shows its own.
            try
            {
                RDFormsUI.Start(forms.Server.Platform);
            }
            catch (AutomationException failure)
            {
                throw new AutomationException(ExternalProtocol.NoDialogs, failure.Message);
            }
        }

        // the dialog is a call of the forms' program: what that program's forms raise while it waits is handled as the answer to it, and what another
        // program's forms raise is not.
        var answer = RDFormsUI.Run(forms.Server.Program, () =>
        {
            var dialog = create();
            var owner = forms.Server.Forms.FirstOrDefault(form => !form.IsDestroyed && form.Window.IsActive)?.Window;
            var waiting = forms.Server.Forms.Where(form => !form.IsDestroyed && form.Window.IsEnabled && !ReferenceEquals(form.Window, owner)).ToList();
            foreach (var form in waiting)
            {
                form.Window.IsEnabled = false;
            }

            try
            {
                // the close is posted, so that it is made once the dialog is shown, and never on a dialog that closed meanwhile.
                using var stopping = token.Register(() => Dispatcher.UIThread.Post(
                    () =>
                    {
                        if (dialog.IsVisible)
                        {
                            close(dialog);
                        }
                    },
                    DispatcherPriority.Send));

                var shown = show(dialog, owner);
                if (!shown.IsCompleted)
                {
                    var frame = new DispatcherFrame();
                    _ = shown.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false, DispatcherPriority.Send), TaskScheduler.Default);
                    Dispatcher.UIThread.PushFrame(frame);
                }

                // the dialog completes on the UI thread, which is the thread that waits for it here.
#pragma warning disable VSTHRD002
                return shown.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            }
            finally
            {
                foreach (var form in waiting.Where(form => !form.IsDestroyed))
                {
                    form.Window.IsEnabled = true;
                }
            }
        });

        token.ThrowIfCancellationRequested();
        return answer;
    }
}
