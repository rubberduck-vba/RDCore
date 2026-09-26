using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <inheritdoc cref="ICallableBindingFactory"/>
/// <remarks>
/// One fact decides it: a member of the workspace has an instruction list somewhere, a member of anything
/// else has a <see cref="SymbolProperties.ExternalTarget"/>. Only whoever contributed the symbol could know
/// which, so the symbol carries it and this reads it — no name matching, no probing, no ordering.
/// <para>
/// A workspace procedure whose body is missing is still bound to the invoker, and still reports the internal
/// error it did before: a missing body there is a real gap in the platform, not a member that lives
/// elsewhere.
/// </para>
/// </remarks>
/// <param name="Invoker">Runs the workspace's own procedures.</param>
/// <param name="External">
/// Reaches everything else. <see langword="null"/> for a session composed without a standard library, where
/// an external member binds to nothing and says so when called rather than looking like a missing procedure.
/// </param>
public sealed record class RuntimeCallableBindingFactory(
    IProcedureInvoker Invoker,
    IExternalDispatcher? External = null) : ICallableBindingFactory
{
    /// <inheritdoc/>
    public ICallableBinding ForMember(
        VBTypeMemberSymbol member, IRuntimeValue? receiver = null, SourceLocation callSite = default)
        => member.GetProperty(SymbolProperties.ExternalTarget) is { Length: > 0 } && External is not null
            ? new ExternalBindingHandle(member, External, receiver, callSite)
            : new CallableBindingHandle(member, Invoker, receiver);
}
