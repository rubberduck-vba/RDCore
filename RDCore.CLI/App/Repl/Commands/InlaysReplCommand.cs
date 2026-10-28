using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Commands;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>INLAYS</c>: shows the hints of the language server in a listing, or stops showing them.
/// </summary>
/// <remarks>
/// <c>INLAYS ON</c> - or <c>INLAYS</c> alone - has <c>LIST</c> show, faint and before each argument of a call, the name of the parameter it is given to
/// (<c>textDocument/inlayHint</c>); <c>INLAYS OFF</c> lists the program as it is typed. The hints are not part of the program.
/// </remarks>
internal sealed class InlaysReplCommand : IReplCommand, IReplCommandDescriptor
{
    private const string On = "ON";
    private const string Off = "OFF";

    public string Name => ReplCommandNames.Inlays;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Inlays_Summary;

    public VerbDescriptor Descriptor => new()
    {
        Name = Name,
        Summary = Summary,
        Forms =
        [
            [
                new VerbArgument
                {
                    Name = "state",
                    Kind = VerbArgumentKind.Choice,
                    Values = [new() { Value = On, Description = Resources.Repl_Inlays_On }, new() { Value = Off, Description = Resources.Repl_Inlays_Off }],
                },
            ],
        ],
    };

    public Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        var text = arguments.Trim();
        if (text.Length == 0 || text.Equals(On, StringComparison.OrdinalIgnoreCase))
        {
            context.Inlays.Enabled = true;
            context.Console.WriteLine(Resources.Repl_Inlays_On);
        }
        else if (text.Equals(Off, StringComparison.OrdinalIgnoreCase))
        {
            context.Inlays.Enabled = false;
            context.Console.WriteLine(Resources.Repl_Inlays_Off);
        }
        else
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
        }

        return Task.FromResult(ReplCommandResult.Continue);
    }
}
