using RDCore.SDK.Model.Errors.Abstract;
using Microsoft.Extensions.Logging;
using RDCore.CLI.Host;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/execute</c>: loads the procedures of a parsed module into the session's code and runs one of
/// them in this host's runtime session, answering with everything it printed.
/// </summary>
/// <remarks>
/// The language server has already parsed the module and defined its symbols by the time this
/// arrives, so each procedure's body is keyed by the session symbol it belongs to — the same
/// <c>SemanticId</c> the invoker looks a callee up by, which is what lets one procedure of the module
/// call another, and one of another module that was loaded before it.
/// <para>
/// The request's cancellation token reaches the interpreter's own fetch-decode loop, so cancelling
/// the request stops a program that would otherwise never stop on its own.
/// </para>
/// </remarks>
internal sealed class HostExecuteHandler(
    IEnvironmentSessionProvider sessionProvider,
    IVerboseMessageBuilder messages,
    ILogger<HostExecuteHandler> logger) : RDCoreRequestHandler<HostExecuteParams, ExecuteSessionResult>
{
    protected override Task<ExecuteSessionResult> HandleAsync(HostExecuteParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return Task.FromResult(NotFound("there is no runtime session to run in"));
        }

        var payload = PlatformJson.Deserialize<HostExecutePayload>(request.Json);
        if (payload?.ParseResult.SyntaxTree is null)
        {
            return Task.FromResult(NotFound("the request carried no parsed module"));
        }

        var session = sessionProvider.Session;
        if (!TryResolveModule(session, request.ModuleName, out var module))
        {
            return Task.FromResult(NotFound($"module '{request.ModuleName}' is not defined in the session"));
        }

        // every procedure of the module, lowered and loaded into the session's code under the symbol it belongs to, so a
        // call from one to another - or from one module to another - resolves through the invoker like any other.
        var errors = new ModuleLoader(session, sessionProvider.Image, messages).Load(module, payload.ParseResult);
        if (errors.Length > 0)
        {
            return Task.FromResult(new ExecuteSessionResult
            {
                Outcome = ExecutionOutcome.SyntaxError,
                Diagnostics = [.. errors],
            });
        }

        if (!session.Symbols.TryResolveValue(request.EntryPoint, module, out var entry) || entry is not VBTypeMemberSymbol entryPoint)
        {
            return Task.FromResult(NotFound($"'{request.ModuleName}.{request.EntryPoint}' is not a procedure of the module"));
        }

        // the pipeline is composed per run: the cancellation is this run's own.
        var pipeline = RuntimeExecutionPipeline.Create(session, sessionProvider.Image, messages, token);

        var output = new RuntimeOutputBuffer();
        var result = Run(session, pipeline, entryPoint, output, token);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("▶️ {module}.{entryPoint}: {outcome}, {lines} line(s) of output.",
                request.ModuleName, request.EntryPoint, result.Outcome, result.Output.Count);
        }

        return Task.FromResult(result);
    }

    private ExecuteSessionResult Run(
        IRuntimeSession session,
        RuntimeExecutionPipeline pipeline,
        VBTypeMemberSymbol entryPoint,
        RuntimeOutputBuffer output,
        CancellationToken token)
    {
        // the session is this host's own and outlives the run; the output buffer and the cancellation
        // are this run's, so the pipeline is composed per run and the output routed for its duration.
        sessionProvider.Output.Target = output;
        try
        {
            return Report(pipeline.Invoker.Invoke(entryPoint, session.Symbols.Resolver, []), session, output, token);
        }
        finally
        {
            sessionProvider.Output.Target = NullRuntimeOutput.Instance;

            // what the program held for as long as it ran is released, and was freed in the order it was allocated in: the free memory at the end of
            // the space is unused again, and what is free afterwards is fragmentation - a hole with something allocated after it.
            session.Memory.Reclaim();
        }
    }

    // What the invoker answered, as the caller sees it. An internal error means the interpreter met
    // something it has no implementation for - unless the run was cancelled, in which case that is
    // exactly what an interrupted run looks like from here.
    private ExecuteSessionResult Report(
        RuntimeSemanticsEvaluationResult invocation, IRuntimeSession session, RuntimeOutputBuffer output, CancellationToken token)
    {
        if (invocation.IsSuccess)
        {
            return new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed, Output = output.Lines };
        }

        if (invocation.IsInternalError)
        {
            return new ExecuteSessionResult
            {
                Outcome = token.IsCancellationRequested ? ExecutionOutcome.Interrupted : ExecutionOutcome.NotImplemented,
                Output = output.Lines,
                ErrorMessage = token.IsCancellationRequested
                    ? "the program was interrupted"
                    : "the interpreter reached something it cannot run yet",
            };
        }

        // the error itself says what and where; the session's own error state says the rest, because that is
        // where Err lives - its Source, and the stack trace captured when the error was raised.
        var error = invocation.ErrorInfo!;
        return new ExecuteSessionResult
        {
            Outcome = ExecutionOutcome.RuntimeError,
            Output = output.Lines,
            ErrorNumber = error.ErrorId,
            ErrorMessage = error.Description,
            ErrorCode = error.ToDiagnosticCode(),
            ErrorTitle = error.AsErrorInfo.ToDiagnosticTitle(),
            ErrorLineNumber = session.Errors.LineNumber,
            // MS-VBAL 6.1.3.2.2.6: unspecified, Source is the current project name - which the session
            // does not know and this does.
            ErrorSource = session.Errors.Source is { Length: > 0 } source ? source : sessionProvider.ProjectName,
            ErrorLine = error.Location.Range.Start.Line,
            ErrorCharacter = error.Location.Range.Start.Character,
            StackTrace =
            [
                .. session.Errors.StackTrace.Frames.Select(frame => new ExecuteStackFrame(
                    frame.ProcedureName,
                    frame.Location?.Range.Start.Line ?? -1,
                    frame.Location?.Range.Start.Character ?? -1)),
            ],
        };
    }

    // the module symbol is defined in the session from the .rdproj, under the global scope.
    private static bool TryResolveModule(IRuntimeSession session, string moduleName, out Symbol module)
    {
        if (session.Symbols.TryResolveValue(moduleName, GlobalSymbols.UnresolvedSymbol, out var resolved) && resolved is not null)
        {
            module = resolved;
            return true;
        }

        module = GlobalSymbols.UnresolvedSymbol;
        return false;
    }

    private static ExecuteSessionResult NotFound(string reason)
        => new() { Outcome = ExecutionOutcome.NotFound, ErrorMessage = reason };
}
