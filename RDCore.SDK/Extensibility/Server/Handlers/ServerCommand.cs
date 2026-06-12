using Newtonsoft.Json.Linq;

namespace RDCore.SDK.Extensibility.Server.Handlers;

public interface ICommandArgsParser<out TArgs> where TArgs : class, new()
{
    TArgs? Parse(JArray? args);
}

public abstract class ServerCommand(string name)
{
    public string Name { get; } = name;

    public abstract Task ExecuteAsync(CancellationToken token, JArray? args = default);
}

public abstract class ServerCommand<TArgs>(ICommandArgsParser<TArgs> CommandArgsParser, string name) : ServerCommand(name)
    where TArgs: class, new()
{
    public override async Task ExecuteAsync(CancellationToken token, JArray? args = default)
    {
        if (CommandArgsParser.Parse(args) is TArgs commandArgs)
        {
            await ExecuteCommandAsync(commandArgs, token);
        }
    }

    protected abstract Task ExecuteCommandAsync(TArgs args, CancellationToken token);
}