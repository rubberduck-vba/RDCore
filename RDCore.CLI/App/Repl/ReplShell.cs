using Microsoft.Extensions.Logging;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;

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
    ReplDocument document,
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
    /// What the shell knows of the program under a debugger. A break at the keyboard pauses a program that runs under one, and cancels what does not.
    /// </summary>
    private ReplDebugger? _debugger;

    /// <summary>
    /// Runs the shell until <c>EXIT</c>, end of input, or <paramref name="token"/>.
    /// </summary>
    /// <param name="token">Cancelled when the host is shutting down — a lost language server, say.</param>
    public async Task RunAsync(CancellationToken token)
    {
        using var breakHandler = new ConsoleBreakHandler(OnBreak);
        var context = new ReplCommandContext(program, console, platform, document, dispatcher.Commands);
        _debugger = context.Debugger;

        await dispatcher.DispatchAsync(context, "SPLASH", string.Empty, CancellationToken.None);
        await dispatcher.DispatchAsync(context, "MEMORY", string.Empty, CancellationToken.None);
        WriteReady();

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
                await SynchronizeDocumentAsync(token);
                return ReplCommandResult.Continue;

            case ReplInputKind.DeleteLine:
                if (!program.Delete(input.LineNumber))
                {
                    console.WriteMessage(MessageKind.Error, Resources.Repl_UndefinedLine, input.LineNumber.ToString());
                }
                await SynchronizeDocumentAsync(token);
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
                await RunImmediateAsync(context, input.Text, token);
                WriteReady();
                return ReplCommandResult.Continue;
        }
    }

    // the server is told of an edit as it is made; a server that is gone costs the shell its diagnostics and nothing it needs to run a program.
    private async Task SynchronizeDocumentAsync(CancellationToken token)
    {
        try
        {
            await document.SynchronizeAsync(token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "The language server could not be told of an edit of the program.");
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

    /// Runs one unnumbered statement on the spot. It is compiled as a procedure of the same module
    /// the program lives in, so it sees the same scope the program does, and a break cancels it the
    /// same way it cancels a RUN.
    private async Task RunImmediateAsync(ReplCommandContext context, string statement, CancellationToken token)
    {
        using var running = CancellationTokenSource.CreateLinkedTokenSource(token);
        _running = running;
        try
        {
            await ReplExecution.ExecuteAsync(
                context, program.ToImmediateModuleSource(statement), ReplProgram.ImmediateEntryPointName, running.Token, immediate: true);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            console.WriteLine(Resources.Repl_Break);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Immediate-mode execution failed.");
            console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, exception.Message);
        }
        finally
        {
            _running = null;
        }
    }

    private void WriteReady() => console.WriteLine($"✅ {Resources.Repl_Ready}");

    // a break at the prompt is just a break; a break during a command cancels it.
    private void OnBreak()
    {
        // A program that runs under a debugger is paused, not cancelled: a cancelled request has no answer to say where the program stopped, and the request that
        // runs it answers when it has.
        if (_debugger?.State is ReplDebugState.Running)
        {
            _ = platform.PauseAsync(CancellationToken.None);
            return;
        }

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
