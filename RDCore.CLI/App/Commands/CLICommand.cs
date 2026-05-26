namespace RDCore.CLI.App.Commands;

internal abstract record class CLICommand(string Name, string? Alias = default) 
{
    public abstract void Execute();
}
