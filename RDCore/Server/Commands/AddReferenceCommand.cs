using Newtonsoft.Json.Linq;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Server.Commands;
using System.Diagnostics;

namespace RDCore.LanguageServer.Server.Commands;

public enum AddRemoveReferenceAction
{
    Add,
    Remove
}

public record class AddRemoveReferenceParams
{
    public AddRemoveReferenceAction Action { get; init; }
    public string? Name { get; init; }
    public string? Path { get; init; }
    public Guid? Guid { get; init; }
}

internal class AddRemoveReferenceParamsParser : ICommandParamsParser<AddRemoveReferenceParams>
{
    public AddRemoveReferenceParams? Parse(JArray? args)
    {
        if (args is JArray arr && arr.Count == 1)
        {
            var path = args[0].Value<string>();
            if (path is not null)
            {
                return new AddRemoveReferenceParams { Path = path };
            }
            else
            {
                var guid = args[0].Value<Guid?>();
                if (guid.HasValue)
                {
                    return new AddRemoveReferenceParams { Guid = guid };
                }
            }
        }
        return null;
    }
}

internal class AddReferenceCommand(IProjectFileService service, ICommandParamsParser<AddRemoveReferenceParams> parser)
    : ServerCommand<AddRemoveReferenceParams>(parser, nameof(AddReferenceCommand))
{
    protected override async Task ExecuteAsync(AddRemoveReferenceParams? args, CancellationToken token)
    {
        Debug.Assert(args?.Action == AddRemoveReferenceAction.Add);
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
