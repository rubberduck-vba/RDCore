using Microsoft.Extensions.Logging;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The interactive RD-VBA shell: <c>rdc.exe</c> with no arguments.
/// </summary>
/// <remarks>
/// An ordinary LSP client session — the same bring-up, the same connection, the same teardown as
/// <c>rdc.exe --workspace</c> — that reads lines instead of exiting once it is attached. A line with
/// a leading number is program text, a line naming a shell command is one, and anything else is a
/// statement to run on the spot; see <see cref="ReplInputParser"/>, which owns those rules.
/// <para>
/// The shell talks to the language server and to nothing else. Every capability it uses — running a
/// program, reading session memory — is one the language server answered for in the platform
/// handshake, so a platform assembled without the component behind one simply reports the command as
/// unavailable rather than failing somewhere deeper.
/// </para>
/// </remarks>
internal sealed class ReplShell(
    ReplProgram program,
    IReplConsole console,
    IReplPlatformClient platform,
    IReplCommandDispatcher dispatcher,
    ILogger<ReplShell> logger)
{
    /// <summary>
    /// How long the language server may wait for the runtime session to come up before the shell
    /// gives up and reports that there is none. Platform bring-up launches three processes.
    /// </summary>
    private const int SessionWaitMilliseconds = 30_000;

    /// <summary>
    /// Cancels whatever command is currently running. Replaced per command; <c>null</c> while the
    /// shell is sitting at the prompt, which is how the break handler tells the two apart.
    /// </summary>
    private CancellationTokenSource? _running;

    /// <summary>
    /// Runs the shell until <c>EXIT</c>, end of input, or <paramref name="token"/>.
    /// </summary>
    /// <param name="token">Cancelled when the host is shutting down — a lost language server, say.</param>
    public async Task RunAsync(CancellationToken token)
    {
        using var breakHandler = new ConsoleBreakHandler(OnBreak);
        var context = new ReplCommandContext(program, console, platform, dispatcher.Commands);

        await WriteSessionBannerAsync(token);

        while (!token.IsCancellationRequested)
        {
            var line = await ReadLineAsync(token);
            if (line is null)
            {
                // end of input: a piped script ran out, or the terminal closed.
                return;
            }

            if (await HandleAsync(context, line, token) == ReplCommandResult.Exit)
            {
                return;
            }
        }
    }

    private async Task<ReplCommandResult> HandleAsync(ReplCommandContext context, string line, CancellationToken token)
    {
        var input = ReplInputParser.Parse(line, dispatcher.IsCommandName);
        switch (input.Kind)
        {
            case ReplInputKind.Empty:
                return ReplCommandResult.Continue;

            case ReplInputKind.StoreLine:
                program.Store(input.LineNumber, input.Text);
                return ReplCommandResult.Continue;

            case ReplInputKind.DeleteLine:
                if (!program.Delete(input.LineNumber))
                {
                    console.WriteMessage(MessageKind.Error, Resources.Repl_UndefinedLine, input.LineNumber.ToString());
                }
                return ReplCommandResult.Continue;

            case ReplInputKind.Command:
                var result = await RunCommandAsync(context, input, token);
                if (result == ReplCommandResult.Continue)
                {
                    // BASIC prints READY. when a direct-mode command finishes - and only then: typing
                    // a numbered line edits the program, it does not run anything.
                    WriteReady();
                }
                return result;

            default:
                // TODO immediate-mode execution: hand the statement to the language server to run.
                console.WriteMessage(MessageKind.Error, Resources.Repl_NotAvailable, input.Text);
                WriteReady();
                return ReplCommandResult.Continue;
        }
    }

    private async Task<ReplCommandResult> RunCommandAsync(ReplCommandContext context, ReplInput input, CancellationToken token)
    {
        // a command gets its own cancellation source so a break stops it without ending the session.
        using var running = CancellationTokenSource.CreateLinkedTokenSource(token);
        _running = running;
        try
        {
            return await dispatcher.DispatchAsync(context, input.CommandName, input.Arguments, running.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            console.WriteLine(Resources.Repl_Break);
            return ReplCommandResult.Continue;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Shell command '{command}' failed.", input.CommandName);
            console.WriteMessage(MessageKind.Error, input.CommandName.ToUpperInvariant(), exception.Message);
            return ReplCommandResult.Continue;
        }
        finally
        {
            _running = null;
        }
    }

    /// <summary>
    /// Prints the session's memory line and the ready banner — in that order, and only once the
    /// runtime session actually exists, so that <c>READY.</c> means the platform really is.
    /// </summary>
    private async Task WriteSessionBannerAsync(CancellationToken token)
    {
        SessionStatusResult status;
        try
        {
            status = await platform.GetSessionStatusAsync(SessionWaitMilliseconds, token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The runtime session status could not be read.");
            status = new SessionStatusResult();
        }

        console.WriteLine();
        console.WriteLine(Resources.Repl_HelpHint);
        console.WriteLine(status.IsComposed
            ? string.Format(Resources.Repl_Memory,
                status.Memory.ReservedBytes, status.Memory.AvailableBytes,
                status.Memory.AllocatedBytes, status.Memory.FreeBytes)
            : Resources.Repl_NoSession);
        WriteReady();
    }

    private void WriteReady() => console.WriteLine(Resources.Repl_Ready);

    // a break at the prompt is just a break; a break during a command cancels it.
    private void OnBreak()
    {
        if (_running is { } running)
        {
            running.Cancel();
            return;
        }

        console.WriteLine();
        console.WriteLine(Resources.Repl_Break);
        WriteReady();
    }

    // Console.ReadLine blocks its thread and nothing can interrupt it, so it runs on a pool thread
    // and the shell waits on the token instead: the host shutting down (a lost language server)
    // must not need the user to press Enter first.
    private static async Task<string?> ReadLineAsync(CancellationToken token)
    {
        try
        {
            return await Task.Run(System.Console.ReadLine, CancellationToken.None).WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
