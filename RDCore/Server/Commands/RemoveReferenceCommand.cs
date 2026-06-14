using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Server.Commands;

namespace RDCore.LanguageServer.Server.Commands;

internal class RemoveReferenceCommand(IProjectFileService service) : ServerCommand<AddRemoveReferenceCommandArgs>(new AddReferenceCommandArgsParser(), nameof(RemoveReferenceCommand))
{
    protected override async Task ExecuteCommandAsync(AddRemoveReferenceCommandArgs args, CancellationToken token)
    {
        if (service.Project.ProjectInfo.References.SingleOrDefault(e => e.Name == args.Name) is RDCoreReference reference)
        {
            service.RemoveReference(reference);
            // TODO also remove from symbols
        }
    }
}
