using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// What the language server answered to a pull of the diagnostics of a document (<c>textDocument/diagnostic</c>).
/// </summary>
/// <param name="ResultId">The identifier of the report, which the shell names when it asks again.</param>
/// <param name="Unchanged">Whether the document did not change since the report the shell named: the report carries no findings, and the shell keeps its own.</param>
/// <param name="Items">The findings; none of an unchanged report.</param>
public sealed record class ReplDiagnosticReport(string? ResultId, bool Unchanged, IReadOnlyList<Diagnostic> Items);

/// <summary>
/// What the language server found in the program the last time the shell asked, and when it asks again.
/// </summary>
/// <remarks>
/// <para>
/// The shell asks for the findings of the program while it waits at the prompt: a moment after the program was last edited, and not for every line of a
/// program that is typed or pasted in a run. It names the report it has, so that a program that did not change since costs an <c>unchanged</c> report and
/// no findings. A listing asks the same way before it marks the lines that have a fix, and so costs the same.
/// </para>
/// <para>
/// A line has a fix when a diagnostic on it carries one in its <c>data</c> - an edit, or a rename (<see cref="DiagnosticFixes"/>) - which is what the code
/// actions of the language server are made of. A listing needs no <c>textDocument/codeAction</c> round trip to know.
/// </para>
/// </remarks>
public sealed class ReplDiagnostics
{
    private readonly SemaphoreSlim _asking = new(1, 1);
    private readonly object _gate = new();
    private string? _resultId;
    private IReadOnlyList<Diagnostic> _items = [];
    private int _generation;
    private CancellationTokenSource? _waiting;

    /// <summary>How long the program is left alone at the prompt before the shell asks about it.</summary>
    internal TimeSpan Idle { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>What waits for <see cref="Idle"/>: the clock, or what a test opens when it says the program was left alone.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>The last question <see cref="RefreshWhenIdle"/> made, or is waiting to make: over once it is answered, or was dropped for a later one.</summary>
    internal Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>The findings of the last answer.</summary>
    public IReadOnlyList<Diagnostic> Items => _items;

    /// <summary>Forgets what was found: the program is another one.</summary>
    public void Clear()
    {
        CancelWaiting();
        lock (_gate)
        {
            // an answer that was asked for before this is the answer for another program: it is not kept.
            _generation++;
            (_resultId, _items) = (null, []);
        }
    }

    /// <summary>
    /// Asks the language server for the findings of the program, naming the report the shell has.
    /// </summary>
    /// <param name="context">The shell's state.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The findings; the last ones when the program is as it was, and none when the server cannot be asked.</returns>
    public async Task<IReadOnlyList<Diagnostic>> RefreshAsync(ReplCommandContext context, CancellationToken token)
    {
        await _asking.WaitAsync(token);
        try
        {
            int generation;
            string? named;
            lock (_gate)
            {
                (generation, named) = (_generation, _resultId);
            }

            if (context.Program.IsEmpty || !await context.Document.EnsureOpenAsync(token) || context.Document.Uri is not { } document)
            {
                Keep(generation, null, [], unchanged: false);
                return _items;
            }

            if (await context.Platform.PullDiagnosticReportAsync(document, named, token) is { } report)
            {
                Keep(generation, report.ResultId, report.Items, report.Unchanged);
            }

            return _items;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // a server that cannot be asked costs a listing its marks and nothing it needs to list.
            return _items;
        }
        finally
        {
            _asking.Release();
        }
    }

    /// <summary>
    /// Asks the language server for the findings once the program was left alone for <see cref="Idle"/>, without making anything wait for the answer.
    /// </summary>
    /// <param name="context">The shell's state.</param>
    public void RefreshWhenIdle(ReplCommandContext context)
    {
        CancelWaiting();
        var waiting = _waiting = new CancellationTokenSource();
        Pending = Task.Run(async () =>
        {
            try
            {
                await Delay(Idle, waiting.Token);
                await RefreshAsync(context, waiting.Token);
            }
            catch (OperationCanceledException)
            {
                // the program was edited again, or another one is there: the next idle asks.
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// The lines of the program a diagnostic with a fix is on.
    /// </summary>
    /// <returns>The zero-based positions of the lines in the program.</returns>
    public IReadOnlySet<int> LinesWithAFix()
    {
        var lines = new HashSet<int>();
        foreach (var diagnostic in _items)
        {
            if (DiagnosticFixes.Of(diagnostic) is { } fixes && (fixes.Rename is not null || fixes.Edit is { Edits.Count: > 0 }))
            {
                lines.Add(diagnostic.Range.Start.Line);
            }
        }

        return lines;
    }

    private void Keep(int generation, string? resultId, IReadOnlyList<Diagnostic> items, bool unchanged)
    {
        lock (_gate)
        {
            if (generation == _generation)
            {
                (_resultId, _items) = (resultId, unchanged ? _items : items);
            }
        }
    }

    private void CancelWaiting()
    {
        var waiting = Interlocked.Exchange(ref _waiting, null);
        waiting?.Cancel();
    }
}
