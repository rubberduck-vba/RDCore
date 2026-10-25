using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI.App.Console;
using RDCore.SDK.Client.Progress;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The progress of the work the language server reports to the shell, drawn at its console (<see cref="ConsoleProgressBar"/>): a bar for each piece of work
/// that is going on, where it does not get in the way of what else is written there.
/// </summary>
/// <remarks>
/// <para>
/// While a line is being edited at the prompt, the work is drawn under it (<see cref="ConsolePromptEditor.Status"/>): what the server does of its own - indexing
/// the workspace as the shell comes up, say - goes on while the line is typed, and the line is typed while it does.
/// </para>
/// <para>
/// While a command runs - <c>ANALYZE</c> - the work done for it is drawn where its output is about to be written, on the line the output starts on, and the
/// line is cleared when the work is done, before the output is written over it. The work the server does of its own is not drawn then: a command's output is
/// written as it comes, and a bar drawn among it would be written over by it. It is drawn again under the next line at the prompt, if it is still going on.
/// </para>
/// </remarks>
internal sealed class ReplProgressDisplay : IDisposable
{
    private const string ClearLine = "\u001b[2K";

    private readonly WorkProgressTracker _tracker;
    private readonly TextWriter _output;
    private readonly Func<int> _width;
    private readonly ConsolePromptEditor? _editor;
    private readonly bool _unicode;
    private readonly object _sync;
    private readonly Dictionary<ProgressToken, int> _frames = [];
    private bool _drawnInline;

    /// <summary>
    /// Draws the progress the tracker follows.
    /// </summary>
    /// <param name="tracker">The work the server reports the progress of.</param>
    /// <param name="output">Where it is drawn.</param>
    /// <param name="width">The cells of a line of the console.</param>
    /// <param name="editor">The editor of the prompt's line, which the work is drawn under while a line is being edited.</param>
    /// <param name="unicode">Whether the bar is drawn in block characters.</param>
    public ReplProgressDisplay(WorkProgressTracker tracker, TextWriter output, Func<int> width, ConsolePromptEditor? editor, bool unicode = true)
    {
        (_tracker, _output, _width, _editor, _unicode) = (tracker, output, width, editor, unicode);
        _sync = editor?.Sync ?? new object();
        if (editor is not null)
        {
            editor.Status = Rows;
        }

        tracker.Changed += OnChanged;
    }

    /// <summary>The lines drawn under the prompt's line: one for each piece of work that is going on.</summary>
    /// <param name="width">The cells of a line of the console.</param>
    public IReadOnlyList<string> Rows(int width)
    {
        lock (_frames)
        {
            return [.. _tracker.Active.Select(state => ConsoleProgressBar.Render(state, width, _frames.GetValueOrDefault(state.Token), _unicode))];
        }
    }

    private void OnChanged(WorkProgressState state)
    {
        lock (_frames)
        {
            if (state.IsDone)
            {
                _frames.Remove(state.Token);
            }
            else
            {
                _frames[state.Token] = _frames.GetValueOrDefault(state.Token) + 1;
            }
        }

        if (_editor?.IsEditing == true)
        {
            _editor.Invalidate();
        }
        else if (state.Origin == WorkProgressOrigin.Request)
        {
            DrawInline();
        }
    }

    // the work done for the command that runs, on the line its output starts on: the last of it begun, which is what the command waits on now.
    private void DrawInline()
    {
        lock (_sync)
        {
            var active = _tracker.Active.LastOrDefault(state => state.Origin == WorkProgressOrigin.Request);
            if (active is not null)
            {
                int frame;
                lock (_frames)
                {
                    frame = _frames.GetValueOrDefault(active.Token);
                }

                _output.Write('\r');
                _output.Write(ClearLine);
                _output.Write(ConsoleProgressBar.Render(active, Math.Max(10, _width()), frame, _unicode));
                _drawnInline = true;
            }
            else if (_drawnInline)
            {
                // the work is done: the line it was drawn on is the output's again.
                _output.Write('\r');
                _output.Write(ClearLine);
                _drawnInline = false;
            }

            _output.Flush();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _tracker.Changed -= OnChanged;
        if (_editor is not null)
        {
            _editor.Status = null;
        }
    }
}
