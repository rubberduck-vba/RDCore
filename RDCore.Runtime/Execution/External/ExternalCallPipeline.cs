using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.StdLib;

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
        => new(session, [new DllImportPolicyInterceptor(), new AutomationPolicyInterceptor(), .. interceptors ?? []], [.. providers]);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Invoke(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
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
                // what the call returned is the value of the expression, which the statement holds until it is over (ObjectReferences.HoldTemporarily):
                // an object a library made for it - CreateObject's, an enumerator - lives until whatever it is assigned to holds it.
                var result = provider.Dispatch(request, resolver);
                if (result.IsSuccess)
                {
                    ObjectReferences.HoldTemporarily(Session, result.Result);
                }

                return result;
            }
        }

        return Unreachable(request);
    }

    // a call of a function of the standard library made without the pipeline, as compiled code makes it: when the only interceptors are the platform's
    // own policies, which refuse automation and library imports but never a function of the library, and when the library is the provider the
    // pipeline would give it to. Any other interceptor, an extension's or an analysis's, keeps the pipeline and sees every call. What the
    // call returns is held as the pipeline holds it.
    internal bool TryCallLibraryDirectly(
        VBTypeMemberSymbol member, IRuntimeValue[] arguments, IRuntimeSymbolResolver resolver, out RuntimeSemanticsEvaluationResult result)
    {
        result = default;
        if (LibraryOf(member) is not { } library)
        {
            return false;
        }

        result = library.Dispatch(new ExternalCallRequest(member, arguments, default), resolver);
        if (result.IsSuccess)
        {
            ObjectReferences.HoldTemporarily(Session, result.Result);
        }

        return true;
    }

    private readonly ConcurrentDictionary<VBTypeMemberSymbol, StdLibDispatcher?> _libraryCalls = new(ReferenceEqualityComparer.Instance);

    // the library a member's calls go to directly, or none: decided once a member, from the member alone (a request without its arguments).
    private StdLibDispatcher? LibraryOf(VBTypeMemberSymbol member)
        => _libraryCalls.TryGetValue(member, out var library) ? library : _libraryCalls.GetOrAdd(member, DirectLibraryOf);

    private StdLibDispatcher? DirectLibraryOf(VBTypeMemberSymbol member)
    {
        // a member of a class is called on an object, which a provider may answer for by the object: only a module's function is decided by itself.
        var request = new ExternalCallRequest(member, [], default);
        if (StdLibDispatcher.HasReceiver(member) || request.IsAutomation || request.IsLibraryImport
            || Interceptors.Any(interceptor => interceptor is not (DllImportPolicyInterceptor or AutomationPolicyInterceptor)))
        {
            return null;
        }

        return Providers.FirstOrDefault(provider => provider.CanDispatch(request)) as StdLibDispatcher;
    }

    // nothing here can run it. For a library import that is "the library call could not be made", which is
    // what error 48 says; for anything else the platform simply has not got it.
    private static RuntimeSemanticsEvaluationResult Unreachable(ExternalCallRequest request)
        => RuntimeSemanticsEvaluationResult.Error(request switch
        {
            { IsLibraryImport: true } => VBRuntimeErrorInfo.For(VBRuntimeErrorId.ErrorInLoadingDll, request.CallSite,
                $"{request.Describe()} could not be called: this platform has no provider for library imports."),
            // 429 is what VBA reports for a class it cannot get an object of, and the same for a member of one it cannot reach.
            { IsAutomation: true } => VBRuntimeErrorInfo.For(VBRuntimeErrorId.ActiveXComponentCantCreateObject, request.CallSite,
                $"{request.Describe()} could not be made: this platform has no automation servers."),
            _ => VBRuntimeErrorInfo.For(VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, request.CallSite,
                $"{request.Describe()} could not be called: nothing here can reach it."),
        });
}
