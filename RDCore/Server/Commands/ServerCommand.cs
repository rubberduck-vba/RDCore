using Newtonsoft.Json.Linq;

namespace RDCore.Server.Commands;

internal abstract class ServerCommand(string name)
{
    public string Name { get; } = name;
    public abstract void Execute(JArray? args = default);

    public bool CanHandle(string command) => Name == command;
}
