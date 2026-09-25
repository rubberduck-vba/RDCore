using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>ANALYZE</c>: has the platform analyze the program in the buffer and reports what it found,
/// without running anything.
/// </summary>
/// <remarks>
/// The findings come from the platform's own diagnostics providers — the same ones an editor's
/// diagnostics pull fans out to. So the answer grows as analyzers do: today it reports whatever the
/// providers the platform was assembled with have to say, and "nothing to report" names how many
/// answered, because no providers and no findings are not the same thing.
/// </remarks>
internal sealed class AnalyzeReplCommand : IReplCommand
{
    public string Name => "ANALYZE";
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Analyze_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (!context.Platform.Provides<SessionAnalyze>())
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_NotAvailable,
                string.Format(Resources.Repl_NotAvailable_Verbose, nameof(SessionAnalyze)));
            return ReplCommandResult.Continue;
        }

        if (context.Program.IsEmpty)
        {
            return ReplCommandResult.Continue;
        }

        var result = await context.Platform.AnalyzeAsync(context.Program.ToModuleSource(), ReplProgram.ModuleName, token);
        if (result.Diagnostics.Count == 0)
        {
            context.Console.WriteLine(string.Format(Resources.Repl_Analyze_NoFindings, result.Providers));
            context.Console.WriteLine();
            return ReplCommandResult.Continue;
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            context.Console.WriteMessage(SeverityOf(diagnostic.Severity),
                $"{Locate(context.Program, diagnostic)} {diagnostic.Code} {diagnostic.Message}".TrimStart());
        }

        context.Console.WriteLine(string.Format(Resources.Repl_Analyze_Count, result.Diagnostics.Count));
        context.Console.WriteLine();
        return ReplCommandResult.Continue;
    }

    /// <summary>
    /// Where the finding is, in the numbering the user typed rather than the physical line of the
    /// module the buffer rendered to — the shell is the only thing that knows the two are different.
    /// </summary>
    internal static string Locate(ReplProgram program, SessionDiagnostic diagnostic)
    {
        // the rendered module is a procedure header, then one line per buffer line, then End Sub — so
        // physical line 2 is the first numbered line.
        var index = diagnostic.Line - 2;
        var lines = program.Lines().ToArray();
        return index >= 0 && index < lines.Length
            ? $"{lines[index].Number}:{diagnostic.Column}"
            : string.Empty;
    }

    private static MessageKind SeverityOf(SessionDiagnosticSeverity severity) => severity switch
    {
        SessionDiagnosticSeverity.Error => MessageKind.Error,
        SessionDiagnosticSeverity.Warning => MessageKind.Warning,
        SessionDiagnosticSeverity.Hint => MessageKind.Trace,
        _ => MessageKind.Information,
    };
}
