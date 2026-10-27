using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;

namespace RDCore.LanguageServer.Server.Handlers.Workspace;

/// <summary>
/// What the client is told once the workspace was made what its project lists: a program that kept its session, and that its diagnostics are to be pulled again.
/// </summary>
internal static class WorkspaceChangeReport
{
    /// <summary>
    /// Tells the client what a change of the project made of the workspace; nothing, when the project is what it was.
    /// </summary>
    public static void Send(ILanguageServerFacade server, WorkspaceReloadResult result)
    {
        if (result.IsUnchanged)
        {
            return;
        }

        if (result.IsProgramRunning)
        {
            server.Window.ShowMessage(new ShowMessageParams { Type = MessageType.Info, Message = WorkspaceMessages.ProjectReloadedProgramRunning });
        }

        DiagnosticRefresh.SendIfSupported(server);
    }
}
