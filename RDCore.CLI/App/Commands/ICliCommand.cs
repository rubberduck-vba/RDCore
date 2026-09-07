namespace RDCore.CLI.App.Commands;

/// <summary>
/// A verb <c>rdc.exe</c> can dispatch in command mode (no workspace, no LSP connection).
/// </summary>
internal interface ICliCommand
{
    /// <summary>
    /// The primary verb used to invoke the command (e.g. <c>describe-ext</c>).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Alternate verbs that also invoke the command.
    /// </summary>
    IReadOnlyList<string> Aliases { get; }

    /// <summary>
    /// A one-line description shown in the command listing.
    /// </summary>
    string Summary { get; }

    /// <summary>
    /// Runs the command against the verb arguments (everything after the verb).
    /// </summary>
    /// <returns>A process exit code: <c>0</c> on success.</returns>
    Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token);
}
