using Newtonsoft.Json.Linq;
using RDCore.Workspace;
using RDCore.Workspace.Services;

namespace RDCore.Server.Commands;

internal class RemoveReferenceCommand(IProjectFileService service) : ServerCommand(nameof(RemoveReferenceCommand))
{
    public override void Execute(JArray? args = null)
    {
        if (args is JArray arr && arr.Count == 1)
        {
            var name = args[0].Value<string>();
            if (name is not null)
            {
                Execute(name);
            }
        }
    }

    private void Execute(string name)
    {
        if (service.Project.ProjectInfo.References.SingleOrDefault(e => e.Name == name) is RDCoreReference reference)
        {
            service.RemoveReference(reference);
            // TODO also remove from symbols
        }
    }
}
