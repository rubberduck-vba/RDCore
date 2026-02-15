using Newtonsoft.Json.Linq;
using RDCore.Workspace;
using RDCore.Workspace.Services;

namespace RDCore.Server.Commands;

internal class AddReferenceCommand(IProjectFileService service) : ServerCommand(nameof(AddReferenceCommand))
{
    public override void Execute(JArray? args = default)
    {
        if (args is JArray arr && arr.Count == 1)
        {
            var path = args[0].Value<string>();
            if (path is not null)
            {
                Execute(path);
            }
            else
            {
                var guid = args[0].Value<Guid?>();
                if (guid.HasValue)
                {
                    Execute(guid.Value);
                }
            }
        }
    }

    private void Execute(string path)
    {
        // TODO actually find the library (or throw)
        var reference = new RDCoreReference
        {
            Name = path,
            AbsolutePath = path,
        };
        Execute(reference);
    }

    private void Execute(Guid guid)
    {
        // TODO actually find the library (or throw)
        var reference = new RDCoreReference
        {
            Name = guid.ToString(),
            Guid = guid,
        };
        Execute(reference);
    }

    private void Execute(RDCoreReference reference)
    {
        service.AddReference(reference);
        // TODO add to symbols
    }
}
