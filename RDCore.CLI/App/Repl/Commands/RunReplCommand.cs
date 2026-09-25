namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>RUN</c>: runs the program in the buffer, from its lowest-numbered line.
/// </summary>
/// <remarks>
/// The buffer renders to a real VBA module whose single procedure holds every numbered line as a
/// labelled statement, so "run the program" is "invoke that procedure" — which is why a <c>GoTo</c>
/// to a line number in the buffer works without the shell knowing anything about it.
/// <para>
/// A break at the keyboard cancels the request, and that cancellation reaches the interpreter loop
/// itself: a program that loops forever still stops.
/// </para>
/// </remarks>
internal sealed class RunReplCommand : IReplCommand
{
    public string Name => "RUN";
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Run_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (context.Program.IsEmpty)
        {
            // nothing to run is not an error, any more than RUN on an empty machine is.
            return ReplCommandResult.Continue;
        }

        await ReplExecution.ExecuteAsync(context, context.Program.ToModuleSource(), ReplProgram.EntryPointName, token);
        return ReplCommandResult.Continue;
    }
}
