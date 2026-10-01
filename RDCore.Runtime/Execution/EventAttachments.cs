using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Runtime.Semantics;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Keeps the handlers of a <c>WithEvents</c> variable attached to the object the variable holds
/// (<strong>MS-VBAL §5.4.3.9</strong>): an assignment detaches them from the object it replaces, and attaches them to
/// the one it stores.
/// </summary>
/// <remarks>
/// The handlers are those of the object that owns the variable, which is the <c>Me</c> of the code assigning it: a
/// <c>WithEvents</c> variable is a variable of a class module. Code with no <c>Me</c> has nothing for the handlers to
/// be those of, and attaches nothing.
/// </remarks>
internal static class EventAttachments
{
    /// <summary>
    /// The object whose code <paramref name="context"/> is the scope of, or <see langword="null"/> when it is not a
    /// member of a class module.
    /// </summary>
    /// <param name="session">The session the code runs in.</param>
    /// <param name="context">The scope of the code.</param>
    public static VBRuntimeObjectId? MeOf(IRuntimeSession session, RuntimeEvaluationContext context)
    {
        var resolver = session.Symbols.Resolver;
        if (resolver.ResolveValue(Tokens.Me, ScopeKind.Local, context.Scope).Symbol is not ITypedSymbol me)
        {
            return null;
        }

        return me.ResolvedType.CreateValue(resolver.GetValue((Symbol)me)) is VBObjectValue { } value && !value.IsNothing()
            ? value.Value
            : null;
    }

    /// <summary>
    /// Detaches the handlers of <paramref name="variable"/> from the object it holds, which an assignment is about to
    /// replace.
    /// </summary>
    /// <param name="session">The session the assignment runs in.</param>
    /// <param name="context">The scope of the assignment.</param>
    /// <param name="variable">The <c>WithEvents</c> variable assigned.</param>
    /// <param name="held">The object it holds, or <see langword="null"/> when none.</param>
    public static void Detach(IRuntimeSession session, RuntimeEvaluationContext context, Symbol variable, VBObjectValue? held)
    {
        if (held is { } source && !source.IsNothing() && MeOf(session, context) is { } owner)
        {
            session.Objects.DetachEventHandlers(source.Value, owner, variable);
        }
    }

    /// <summary>
    /// Attaches the handlers of <paramref name="variable"/> to the object an assignment stored in it.
    /// </summary>
    /// <param name="session">The session the assignment runs in.</param>
    /// <param name="context">The scope of the assignment.</param>
    /// <param name="variable">The <c>WithEvents</c> variable assigned.</param>
    /// <param name="assigned">The object it now holds, or <see langword="null"/> when none.</param>
    public static void Attach(IRuntimeSession session, RuntimeEvaluationContext context, Symbol variable, VBObjectValue? assigned)
    {
        if (assigned is { } source && !source.IsNothing() && MeOf(session, context) is { } owner)
        {
            session.Objects.AttachEventHandlers(source.Value, owner, variable);
        }
    }
}
