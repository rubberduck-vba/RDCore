using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Server.Commands;
using System.Diagnostics;

namespace RDCore.LanguageServer.Server.Commands;

internal class RemoveReferenceCommand(IProjectFileService service, ICommandParamsParser<AddRemoveReferenceParams> parser) 
    : ServerCommand<AddRemoveReferenceParams>(parser, nameof(RemoveReferenceCommand))
{
    protected override async Task ExecuteAsync(AddRemoveReferenceParams? args, CancellationToken token)
    {
        Debug.Assert(args?.Action == AddRemoveReferenceAction.Remove);
        if (service.Project.ProjectInfo.References.SingleOrDefault(e => e.Name == args?.Name) is RDCoreReference reference)
        {
            service.RemoveReference(reference);
            // TODO also remove from symbols
        }
    }
}
