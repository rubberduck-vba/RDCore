using Newtonsoft.Json.Linq;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Server.Commands;

namespace RDCore.LanguageServer.Server.Commands;

public class AddRemoveReferenceCommandArgs
{
    public string? Name { get; init; }
    public string? Path { get; init; }
    public Guid? Guid { get; init; }
}

internal class AddReferenceCommandArgsParser : ICommandArgsParser<AddRemoveReferenceCommandArgs>
{
    public AddRemoveReferenceCommandArgs? Parse(JArray? args)
    {
        if (args is JArray arr && arr.Count == 1)
        {
            var path = args[0].Value<string>();
            if (path is not null)
            {
                return new AddRemoveReferenceCommandArgs { Path = path };
            }
            else
            {
                var guid = args[0].Value<Guid?>();
                if (guid.HasValue)
                {
                    return new AddRemoveReferenceCommandArgs { Guid = guid };
                }
            }
        }
        return default;
    }
}

internal class AddReferenceCommand(IProjectFileService service) : ServerCommand<AddRemoveReferenceCommandArgs>(new AddReferenceCommandArgsParser(), nameof(AddReferenceCommand))
{
    protected override Task ExecuteCommandAsync(AddRemoveReferenceCommandArgs args, CancellationToken token)
    {
        throw new NotImplementedException();
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
