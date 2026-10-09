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
            return Task.FromResult(NotFound(Resources.Host_NoRuntimeSessionToRunIn));
        }

        // a program that is running owns the session: nothing of it is loaded, composed or redefined under it.
        if (sessionProvider.Execution.State is ProgramState.Running)
        {
            return Task.FromResult(new ExecuteSessionResult { Outcome = ExecutionOutcome.Refused, ErrorMessage = Resources.Host_AProgramIsRunning });
        }

        var payload = PlatformJson.Deserialize<HostExecutePayload>(request.Json);
        if (payload?.ParseResult.SyntaxTree is null)
        {
            return Task.FromResult(NotFound(Resources.Host_RequestCarriedNoParsedModule));
        }

        var session = sessionProvider.Session;
        if (!TryResolveModule(session, request.ModuleName, out var module))
        {
            return Task.FromResult(NotFound(string.Format(Resources.Host_ModuleIsNotDefined, request.ModuleName)));
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
            return Task.FromResult(NotFound(string.Format(Resources.Host_NotAProcedureOfTheModule, request.ModuleName, request.EntryPoint)));
        }

        // the pipeline is composed per run: the cancellation is this run's own. For a program under a debugger a cancellation is a break and not the end of the
        // run, which the owner of the program sees to.
        var pipeline = RuntimeExecutionPipeline.Create(session, sessionProvider.Image, messages, request.Debug ? CancellationToken.None : token);

        return RunAsync(pipeline, entryPoint, request, token);
    }

    private async Task<ExecuteSessionResult> RunAsync(RuntimeExecutionPipeline pipeline, VBTypeMemberSymbol entryPoint, HostExecuteParams request, CancellationToken token)
    {
        var result = await sessionProvider.Execution.RunAsync(pipeline, entryPoint, request.Debug, token, request.Immediate);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("▶️ {module}.{entryPoint}: {outcome}, {lines} line(s) of output.",
                request.ModuleName, request.EntryPoint, result.Outcome, result.Output.Count);
        }

        return result;
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
