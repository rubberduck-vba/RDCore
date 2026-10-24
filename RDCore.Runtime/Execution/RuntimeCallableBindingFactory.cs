using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Runtime.CompilerServices;

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
    {
        // a procedure of a standard module, or of a library's, is bound to nothing but itself: one handle for every call of it (IR lowering S1.3, S1.9).
        if (receiver is null && callSite == default && _procedures.TryGetValue(member, out var binding))
        {
            return binding;
        }

        if (IsExternal(member) && External is not null)
        {
            return receiver is null && callSite == default
                ? _procedures.GetValue(member, procedure => new ExternalBindingHandle(procedure, External))
                : new ExternalBindingHandle(member, External, receiver, callSite);
        }

        return receiver is null
            ? _procedures.GetValue(member, procedure => new CallableBindingHandle(procedure, Invoker))
            : new CallableBindingHandle(member, Invoker, receiver);
    }

    private readonly ConditionalWeakTable<VBTypeMemberSymbol, ICallableBinding> _procedures = new();

    // a Declare names a native library: it has no body in the workspace and no target the standard library knows, and what it is is in its type.
    private static bool IsExternal(VBTypeMemberSymbol member)
        => member is VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol
            || member.GetProperty(SymbolProperties.ExternalTarget) is { Length: > 0 };
}
