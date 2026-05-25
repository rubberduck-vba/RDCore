using RDCore.SDK.Server.Commands;
using RDCore.Workspace;
using RDCore.Workspace.Services;

namespace RDCore.Server.Commands;

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
