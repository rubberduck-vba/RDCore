using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;

namespace RDCore.LanguageServer.Server.Handlers.Workspace;

/// <summary>
/// What the user is told once the workspace was made what its project lists: a program that kept its session. That what the client shows of each document is to
/// be refreshed is told by the reload itself (<see cref="IWorkspaceRefresh"/>).
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
    }
}
