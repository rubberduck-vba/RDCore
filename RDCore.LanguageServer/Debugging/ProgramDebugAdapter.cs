using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using OmniSharp.Extensions.DebugAdapter.Protocol.Server;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using DapThread = OmniSharp.Extensions.DebugAdapter.Protocol.Models.Thread;

namespace RDCore.LanguageServer.Debugging;

/// <summary>
/// What a client says to start a program under the debug adapter, beyond what every <strong>DAP</strong> <c>launch</c> request says.
/// </summary>
public record class ProgramLaunchRequestArguments : LaunchRequestArguments
{
    /// <summary>The programmatic name of the module that has the entry point.</summary>
    public string? Module { get; init; }

    /// <summary>The name of the parameterless procedure to run. <c>Main</c> when the client does not say.</summary>
    public string? EntryPoint { get; init; }
}

/// <summary>
/// Speaks the <strong>Debug Adapter Protocol</strong> for a program that runs on the platform: each request of a debugger client is a request to the component that owns the
/// runtime session, and each place the program waits is an event.
/// </summary>
/// <remarks>
/// The client numbers lines (and columns) from <c>1</c> or from <c>0</c> as it says when it initializes; the platform numbers them from <c>0</c>, and this is where they
/// are converted. A client names files and the platform names modules, and <see cref="IDebugWorkspace"/> knows which is which.
/// <para>
/// A program runs under the request that started it, and that request answers when the program next waits or is over. The adapter answers the client at once instead
/// (<c>continue</c>, <c>next</c>, <c>stepIn</c>, <c>stepOut</c>, and the start of the program on <c>configurationDone</c>), and says where the program is by an event when
/// the request that runs it has answered.
/// </para>
/// <para>
/// 👉 What the program printed is said when the program next waits or is over, and not as it prints: the host answers all of it at once. A line printed by an
/// expression that was evaluated is said when it is evaluated.
/// </para>
/// </remarks>
internal sealed class ProgramDebugAdapter(
    IDebugWorkspace workspace,
    IProgramDebugService debugging,
    IHostOutputRelay output,
    ILogger<ProgramDebugAdapter> logger) :
    ILaunchHandler<ProgramLaunchRequestArguments>,
    IConfigurationDoneHandler,
    ISetBreakpointsHandler,
    IContinueHandler,
    INextHandler,
    IStepInHandler,
    IStepOutHandler,
    IPauseHandler,
    IThreadsHandler,
    IStackTraceHandler,
    IScopesHandler,
    IVariablesHandler,
    IEvaluateHandler,
    IGotoTargetsHandler,
    IGotoHandler,
    ITerminateHandler,
    IDisconnectHandler
{
    /// <summary>The one thread of the program, which is the only one there is.</summary>
    internal const int ThreadId = 1;

    // JSON-RPC 2.0's range for errors the server defines. "Invalid Request" would be taken for a malformed message, and a client would lose the reason.
    private const int RefusedCode = -32000;

    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<int, VariableScope> _references = [];
    private readonly Dictionary<string, HashSet<int>> _breakpointLines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _clientPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // the library says the adapter is ready to be configured as soon as it is initialized, which is before the workspace is brought up: a client that sets breakpoints
    // then is answered when there is a workspace to set them in.
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IDebugAdapterServer? _server;
    private bool _linesStartAt1 = true;
    private bool _columnsStartAt1 = true;
    private string? _module;
    private string _entryPoint = "Main";
    private int _nextReference;
    private bool _stepped;
    private bool _paused;
    private bool _launching;
    private bool _launched;
    private bool _started;
    private int _terminated;

    private sealed record VariableScope(int Frame, HostVariableScope Scope, int Reference);

    /// <summary>Completes when the client has disconnected, or the adapter has nothing left to debug.</summary>
    public Task Closed => _closed.Task;

    /// <summary>
    /// Connects the adapter to the client it speaks to, which exists once the transport is open.
    /// </summary>
    /// <param name="server">The server side of the connection with the client.</param>
    public void Attach(IDebugAdapterServer server)
    {
        _server = server;

        // what the program prints is said as it prints.
        output.Printed += lines =>
        {
            foreach (var line in lines)
            {
                Say(line + "\n", OutputEventCategory.StandardOutput);
            }
        };
    }

    /// <summary>
    /// Takes note of how the client numbers lines and columns.
    /// </summary>
    /// <param name="request">What the client said when it initialized.</param>
    public void Initialize(InitializeRequestArguments request)
    {
        _linesStartAt1 = request.LinesStartAt1;
        _columnsStartAt1 = request.ColumnsStartAt1;
    }

    /// <summary>
    /// The platform lost a component it cannot go on without: the program is over.
    /// </summary>
    /// <param name="component">What was lost.</param>
    public Task LostAsync(string component)
    {
        Say($"the {component} was lost: the program cannot go on.", OutputEventCategory.StandardError);
        return TerminateAsync(exitCode: 1);
    }

    // ---- lines, columns, files ----

    private int ToClientLine(int line) => _linesStartAt1 ? line + 1 : line;

    private int ToClientColumn(int column) => _columnsStartAt1 ? column + 1 : column;

    private int ToLine(long clientLine) => (int)(_linesStartAt1 ? clientLine - 1 : clientLine);

    // a file is named as the client named it when it has: the same file, written as the workspace has it, may not be one the client recognizes.
    private Source? SourceOf(string module)
    {
        lock (_sync)
        {
            if (_clientPaths.TryGetValue(module, out var named))
            {
                return new Source { Name = Path.GetFileName(named), Path = named };
            }
        }

        return workspace.TryGetPath(module, out var path) ? new Source { Name = Path.GetFileName(path), Path = path } : null;
    }

    private static RpcErrorException Refusal(string message) => new(RefusedCode, error: null!, message);

    // ---- start ----

    public async Task<LaunchResponse> Handle(ProgramLaunchRequestArguments request, CancellationToken cancellationToken)
    {
        _launching = true;
        _entryPoint = string.IsNullOrWhiteSpace(request.EntryPoint) ? "Main" : request.EntryPoint;
        _module = request.Module;
        if (string.IsNullOrWhiteSpace(_module))
        {
            var refusal = Refusal("A launch configuration names the module that has the entry point: \"module\".");
            _ = _opened.TrySetException(refusal);
            throw refusal;
        }

        try
        {
            await workspace.OpenAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            _ = _opened.TrySetException(Refusal(exception.Message));
            throw Refusal(exception.Message);
        }

        if (!workspace.TryGetPath(_module, out _))
        {
            var refusal = Refusal($"'{_module}' is not a module of the workspace.");
            _ = _opened.TrySetException(refusal);
            throw refusal;
        }

        _launched = true;
        _ = _opened.TrySetResult();
        return new LaunchResponse();
    }

    public async Task<ConfigurationDoneResponse> Handle(ConfigurationDoneArguments request, CancellationToken cancellationToken)
    {
        // configuration follows a launch, which may still be bringing the workspace up; without one there is nothing to wait for.
        if (!_launching)
        {
            throw Refusal("There is no program to start: launch first.");
        }

        await _opened.Task.WaitAsync(cancellationToken);
        lock (_sync)
        {
            if (!_launched || _started)
            {
                throw Refusal(_started ? "The program was started already." : "There is no program to start: launch first.");
            }

            _started = true;
        }

        Run(token => workspace.StartAsync(_module!, _entryPoint, token));
        return new ConfigurationDoneResponse();
    }

    // ---- breakpoints ----

    public async Task<SetBreakpointsResponse> Handle(SetBreakpointsArguments request, CancellationToken cancellationToken)
    {
        var requested = request.Breakpoints?.Select(breakpoint => ToLine(breakpoint.Line)).ToArray() ?? [];
        await _opened.Task.WaitAsync(cancellationToken);

        if (request.Source?.Path is not { } path || !workspace.TryGetModule(path, out var module))
        {
            return new SetBreakpointsResponse
            {
                Breakpoints = new Container<Breakpoint>(requested.Select(line => new Breakpoint
                {
                    Verified = false,
                    Line = ToClientLine(line),
                    Message = "this file is not part of the workspace",
                })),
            };
        }

        var result = await debugging.SetBreakpointsAsync(module, requested, cancellationToken);
        lock (_sync)
        {
            _breakpointLines[module] = [.. requested];
            _clientPaths[module] = path;
        }

        return new SetBreakpointsResponse
        {
            Breakpoints = new Container<Breakpoint>(result.Breakpoints.Select(breakpoint => new Breakpoint
            {
                Verified = breakpoint.Verified,
                Line = ToClientLine(breakpoint.Line),
                Source = request.Source,
                Message = breakpoint.Verified ? null : "no statement begins on this line",
            })),
        };
    }

    // ---- running ----

    public Task<ContinueResponse> Handle(ContinueArguments request, CancellationToken cancellationToken)
    {
        Resume(step: null);
        return Task.FromResult(new ContinueResponse { AllThreadsContinued = true });
    }

    public Task<NextResponse> Handle(NextArguments request, CancellationToken cancellationToken)
    {
        Resume(StepKind.Over);
        return Task.FromResult(new NextResponse());
    }

    public Task<StepInResponse> Handle(StepInArguments request, CancellationToken cancellationToken)
    {
        Resume(StepKind.Into);
        return Task.FromResult(new StepInResponse());
    }

    public Task<StepOutResponse> Handle(StepOutArguments request, CancellationToken cancellationToken)
    {
        Resume(StepKind.Out);
        return Task.FromResult(new StepOutResponse());
    }

    public async Task<PauseResponse> Handle(PauseArguments request, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _paused = true;
        }

        _ = await debugging.PauseAsync(cancellationToken);
        return new PauseResponse();
    }

    private void Resume(StepKind? step)
    {
        lock (_sync)
        {
            _stepped = step is not null;
            _references.Clear();
        }

        Run(token => debugging.ResumeAsync(step, token));
    }

    // runs a request that answers when the program next waits or is over, on its own: the client has been answered already.
    private void Run(Func<CancellationToken, Task<ExecuteSessionResult>> request)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await request(_lifetime.Token);
                await ReportAsync(result);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "❌ The program could not be run.");
                Say(exception.Message, OutputEventCategory.StandardError);
                await TerminateAsync(exitCode: 1);
            }
        });
    }

    private async Task ReportAsync(ExecuteSessionResult result)
    {
        // the lines the program printed came apart from the answer, and are said before what the answer says of the program.
        await output.WaitForAsync(result.StreamedLines, TimeSpan.FromSeconds(2), _lifetime.Token);

        // what is left in the answer is the line the program left open.
        foreach (var line in result.Output)
        {
            Say(line + "\n", OutputEventCategory.StandardOutput);
        }

        switch (result.Outcome)
        {
            case ExecutionOutcome.Suspended:
                await StoppedAsync();
                break;
            case ExecutionOutcome.Completed:
            case ExecutionOutcome.Halted:
                await TerminateAsync(exitCode: 0);
                break;
            case ExecutionOutcome.Refused:
                // the program is as it was: a request was made that it cannot be asked.
                Say(result.ErrorMessage + "\n", OutputEventCategory.StandardError);
                break;
            default:
                Say(Describe(result) + "\n", OutputEventCategory.StandardError, result);
                await TerminateAsync(exitCode: 1);
                break;
        }
    }

    private static string Describe(ExecuteSessionResult result) => result.Outcome switch
    {
        ExecutionOutcome.SyntaxError => string.Join("\n", result.Diagnostics),
        ExecutionOutcome.RuntimeError => $"{(result.ErrorTitle.Length > 0 ? result.ErrorTitle : "Run-time error")} '{result.ErrorNumber}': {result.ErrorMessage}",
        _ => result.ErrorMessage.Length > 0 ? result.ErrorMessage : result.Outcome.ToString(),
    };

    private async Task StoppedAsync()
    {
        bool stepped;
        bool paused;
        lock (_sync)
        {
            (stepped, paused) = (_stepped, _paused);
            (_stepped, _paused) = (false, false);
        }

        var (reason, description) = stepped ? (StoppedEventReason.Step, "Step")
            : paused ? (StoppedEventReason.Pause, "Paused")
            : await WhyStoppedAsync();

        _server!.SendStopped(new StoppedEvent
        {
            Reason = reason,
            Description = description,
            ThreadId = ThreadId,
            AllThreadsStopped = true,
        });
    }

    // a program that waits without having been asked to is at a breakpoint, or at a Stop statement: the statement it waits before says which.
    private async Task<(StoppedEventReason, string)> WhyStoppedAsync()
    {
        var stack = await debugging.StackAsync(_lifetime.Token);
        if (stack.Frames is [var innermost, ..])
        {
            lock (_sync)
            {
                if (_breakpointLines.TryGetValue(innermost.Module, out var lines) && lines.Contains(innermost.Line))
                {
                    return (StoppedEventReason.Breakpoint, "Paused on breakpoint");
                }
            }
        }

        return (StoppedEventReason.Breakpoint, "Paused on Stop statement");
    }

    private Task TerminateAsync(int exitCode)
    {
        // the program is over once, however many ways there are to learn it.
        if (Interlocked.Exchange(ref _terminated, 1) == 0)
        {
            _server!.SendExited(new ExitedEvent { ExitCode = exitCode });
            _server!.SendTerminated(new TerminatedEvent());
        }

        return Task.CompletedTask;
    }

    // ---- where the program is ----

    public Task<ThreadsResponse> Handle(ThreadsArguments request, CancellationToken cancellationToken)
        => Task.FromResult(new ThreadsResponse { Threads = new Container<DapThread>(new DapThread { Id = ThreadId, Name = "Main" }) });

    public async Task<StackTraceResponse> Handle(StackTraceArguments request, CancellationToken cancellationToken)
    {
        var stack = await debugging.StackAsync(cancellationToken);
        var frames = stack.Frames
            .Select(frame => new StackFrame
            {
                Id = frame.Id,
                Name = frame.Handler is null ? frame.Procedure : $"{frame.Procedure} (in {frame.Handler})",
                Source = SourceOf(frame.Module),
                Line = ToClientLine(frame.Line),
                Column = ToClientColumn(frame.Character),
            })
            .Skip((int)(request.StartFrame ?? 0));

        if (request.Levels is > 0)
        {
            frames = frames.Take((int)request.Levels.Value);
        }

        return new StackTraceResponse { StackFrames = new Container<StackFrame>(frames), TotalFrames = stack.Frames.Count };
    }

    public Task<ScopesResponse> Handle(ScopesArguments request, CancellationToken cancellationToken)
    {
        var frame = (int)request.FrameId;
        lock (_sync)
        {
            return Task.FromResult(new ScopesResponse
            {
                Scopes = new Container<Scope>(
                    new Scope { Name = "Locals", PresentationHint = "locals", VariablesReference = Reference(new VariableScope(frame, HostVariableScope.Locals, 0)) },
                    new Scope { Name = "Module", VariablesReference = Reference(new VariableScope(frame, HostVariableScope.Module, 0)) }),
            });
        }
    }

    // called with the lock held.
    private int Reference(VariableScope scope)
    {
        _references[++_nextReference] = scope;
        return _nextReference;
    }

    public async Task<VariablesResponse> Handle(VariablesArguments request, CancellationToken cancellationToken)
    {
        VariableScope? scope;
        lock (_sync)
        {
            _ = _references.TryGetValue((int)request.VariablesReference, out scope);
        }

        if (scope is null)
        {
            return new VariablesResponse { Variables = new Container<Variable>() };
        }

        var result = await debugging.VariablesAsync(scope.Frame, scope.Scope, scope.Reference, cancellationToken);
        IEnumerable<HostVariable> variables = result.Variables;
        if (request.Start is > 0)
        {
            variables = variables.Skip((int)request.Start.Value);
        }

        if (request.Count is > 0)
        {
            variables = variables.Take((int)request.Count.Value);
        }

        lock (_sync)
        {
            return new VariablesResponse
            {
                Variables = new Container<Variable>(variables.Select(variable => new Variable
                {
                    Name = variable.Name,
                    Value = variable.Value,
                    Type = variable.Type,
                    VariablesReference = variable.Reference == 0 ? 0 : Reference(scope with { Reference = variable.Reference }),
                }).ToArray()),
            };
        }
    }

    public async Task<EvaluateResponse> Handle(EvaluateArguments request, CancellationToken cancellationToken)
    {
        var frame = (int)(request.FrameId ?? 0);
        var result = await debugging.EvaluateAsync(frame, request.Expression, cancellationToken);

        // what the expression printed is the debug console's, whichever way the value is asked for.
        foreach (var line in result.Output)
        {
            Say(line + "\n", OutputEventCategory.Console);
        }

        if (!result.Success)
        {
            throw Refusal(result.Error ?? "the expression has no value");
        }

        lock (_sync)
        {
            return new EvaluateResponse
            {
                Result = result.Value,
                Type = result.Type,
                VariablesReference = result.Reference == 0 ? 0 : Reference(new VariableScope(frame, HostVariableScope.Locals, result.Reference)),
            };
        }
    }

    // ---- the point the program goes on from ----

    public Task<GotoTargetsResponse> Handle(GotoTargetsArguments request, CancellationToken cancellationToken)
        => Task.FromResult(new GotoTargetsResponse
        {
            Targets = new Container<GotoTarget>(new GotoTarget { Id = request.Line, Label = $"Line {request.Line}", Line = (int)request.Line }),
        });

    public async Task<GotoResponse> Handle(GotoArguments request, CancellationToken cancellationToken)
    {
        var moved = await debugging.GotoAsync(ToLine(request.TargetId), label: null, cancellationToken);
        if (!moved.Moved)
        {
            throw Refusal(moved.Reason ?? "the program cannot go on from there");
        }

        // moving the point is not running: the program waits still, and its client is told where.
        lock (_sync)
        {
            _references.Clear();
        }

        _server!.SendStopped(new StoppedEvent { Reason = StoppedEventReason.Goto, ThreadId = ThreadId, AllThreadsStopped = true });
        return new GotoResponse();
    }

    // ---- over ----

    public async Task<TerminateResponse> Handle(TerminateArguments request, CancellationToken cancellationToken)
    {
        _ = await debugging.TerminateAsync(wipe: true, cancellationToken);
        await TerminateAsync(exitCode: 0);
        return new TerminateResponse();
    }

    public async Task<DisconnectResponse> Handle(DisconnectArguments request, CancellationToken cancellationToken)
    {
        // a program the client started is not left behind it, unless it said so.
        if (request.TerminateDebuggee || !request.Restart)
        {
            _ = await debugging.TerminateAsync(wipe: true, cancellationToken);
        }

        await _lifetime.CancelAsync();
        _ = _closed.TrySetResult();
        return new DisconnectResponse();
    }

    // ---- output ----

    private void Say(string text, OutputEventCategory category, ExecuteSessionResult? located = null)
    {
        if (_server is not { } server)
        {
            return;
        }

        var output = new OutputEvent { Category = category, Output = text };
        if (located is { ErrorLine: >= 0 } && _module is not null && SourceOf(_module) is { } source)
        {
            output = output with { Source = source, Line = ToClientLine(located.ErrorLine) };
        }

        server.SendOutput(output);
    }
}
