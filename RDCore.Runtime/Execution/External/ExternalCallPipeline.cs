using RDCore.SDK.Model.Errors;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Collections.Immutable;

namespace RDCore.Runtime.Execution.External;

/// <inheritdoc cref="IExternalDispatcher"/>
/// <remarks>
/// Every call to anything outside the workspace passes through here, in order: the interceptors see it and
/// may refuse it, then the provider that can reach it runs it. A refused call never reaches a provider, and a
/// call nothing can reach is a run-time error saying so rather than a silent nothing.
/// <para>
/// 🎯 That ordering is the point, not an implementation detail. A native library call is a way to run
/// arbitrary code, and the historical answer has been for administrators to turn the whole language off
/// because nothing could see what a macro did. Here every such call is visible to an interceptor, with its
/// arguments, <em>before</em> it happens — and one refusal stops it.
/// </para>
/// </remarks>
/// <param name="Session">The session whose calls this runs, and whose environment its policy reads.</param>
/// <param name="Interceptors">
/// In the order they see a call. Every one of them sees it; the first to refuse it stops it there.
/// </param>
/// <param name="Providers">
/// The things that can actually run a call, asked in order. Which of them exist is what a platform can do:
/// no provider for a kind of target means a workspace using it is told so.
/// </param>
public sealed record class ExternalCallPipeline(
    IRuntimeSession Session,
    ImmutableArray<IExternalCallInterceptor> Interceptors,
    ImmutableArray<IExternalCallProvider> Providers) : IExternalDispatcher
{

    /// <summary>
    /// Composes the pipeline for <paramref name="session"/>.
    /// </summary>
    /// <param name="session">The session whose calls this runs, and whose environment its policy reads.</param>
    /// <param name="providers">The things that can run a call.</param>
    /// <param name="interceptors">
    /// Anything that wants to see calls before they happen, beyond the platform's own policy — which is
    /// always first, so that nothing an extension does can let through what an administrator refused.
    /// </param>
    public static ExternalCallPipeline For(
        IRuntimeSession session,
        IEnumerable<IExternalCallProvider> providers,
        IEnumerable<IExternalCallInterceptor>? interceptors = null)
        => new(session, [new DllImportPolicyInterceptor(), .. interceptors ?? []], [.. providers]);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Invoke(ExternalCallRequest request, ISymbolResolver resolver)
    {
        foreach (var interceptor in Interceptors)
        {
            if (interceptor.Intercept(request, Session) is { IsBlocked: true } decision)
            {
                // MS-VBAL has a code for exactly this, so a handler written for VBA behaves as its author
                // expected: 70, "Permission denied". The verbose says which call, arguments and all, which is
                // the difference between a diagnostic and a shrug.
                return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                    VBRuntimeErrorId.PermissionDenied, request.CallSite,
                    $"{request.Describe()} was blocked: {decision.Reason}"));
            }
        }

        foreach (var provider in Providers)
        {
            if (provider.CanDispatch(request))
            {
                return provider.Dispatch(request, resolver);
            }
        }

        return Unreachable(request);
    }

    // nothing here can run it. For a library import that is "the library call could not be made", which is
    // what error 48 says; for anything else the platform simply has not got it.
    private static RuntimeSemanticsEvaluationResult Unreachable(ExternalCallRequest request)
        => RuntimeSemanticsEvaluationResult.Error(request.IsLibraryImport
            ? VBRuntimeErrorInfo.For(VBRuntimeErrorId.ErrorInLoadingDll, request.CallSite,
                $"{request.Describe()} could not be called: this platform has no provider for library imports.")
            : VBRuntimeErrorInfo.For(VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, request.CallSite,
                $"{request.Describe()} could not be called: nothing here can reach it."));
}
