using RDCore.External.Automation;
using RDCore.External.Interaction;
using RDCore.External.Protocol;
using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.External.Client;

/// <summary>
/// The dialogs of a program whose user interface is its forms: shown by the external host beside them, the active one as their owner, as a
/// <c>MsgBox</c> is shown over a Visual Basic 6.0 program's forms.
/// </summary>
/// <remarks>
/// A dialog is answered once it is closed, as a modal form is: what the forms raise meanwhile is handled as it comes. A run that is stopped while a dialog is
/// open has the external host close it, and stops waiting for it. An external host that shows no dialogs, cannot be started, or cannot start the windows it
/// shows them in (<see cref="ExternalProtocol.NoDialogs"/>) leaves them to <paramref name="fallback"/>.
/// </remarks>
/// <param name="servers">The external host's servers, which host the program's forms.</param>
/// <param name="fallback">The dialogs shown when the external host shows none.</param>
public sealed class RemoteUserInteraction(RemoteAutomationServer servers, IUserInteraction fallback) : IUserInteraction
{
    /// <inheritdoc/>
    public VBMsgBoxResult ShowMessage(MessageBoxRequest request, CancellationToken token = default)
    {
        if (servers.HasDialogs)
        {
            try
            {
                return servers.ShowMessage(request, token);
            }
            catch (AutomationException failure) when (failure.HResult == ExternalProtocol.NoDialogs)
            {
            }
        }

        return fallback.ShowMessage(request, token);
    }

    /// <inheritdoc/>
    public string? Prompt(InputBoxRequest request, CancellationToken token = default)
    {
        if (servers.HasDialogs)
        {
            try
            {
                return servers.Prompt(request, token);
            }
            catch (AutomationException failure) when (failure.HResult == ExternalProtocol.NoDialogs)
            {
            }
        }

        return fallback.Prompt(request, token);
    }
}
