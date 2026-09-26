using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// A binding that is reached by <em>invoking</em> it, and that reports what happened rather than throwing.
/// </summary>
/// <remarks>
/// <see cref="IBindingHandle.Invoke"/> throws a run-time error, because it has only an
/// <see cref="IRuntimeValue"/> to return; the semantics layer never throws, and needs the error in the
/// result. Both callable bindings have always had a <c>Call</c> that does that — this is the shape they
/// share, so a call site can hold one without knowing whether the code behind it is the workspace's.
/// </remarks>
public interface ICallableBinding : IBindingHandle
{
    /// <summary>
    /// The member this binding invokes.
    /// </summary>
    VBTypeMemberSymbol Member { get; }

    /// <summary>
    /// Invokes it and returns the outcome: the value it yielded, or the run-time error it raised.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context.</param>
    /// <param name="args">The arguments of the call, without the receiver, which the binding passes itself.</param>
    RuntimeSemanticsEvaluationResult Call(ISymbolResolver resolver, IRuntimeValue[] args);
}

/// <summary>
/// Binds a member to the thing that will run it.
/// </summary>
/// <remarks>
/// The one place that decides <em>what kind of code a member is</em>: the workspace's own, which an
/// <see cref="IProcedureInvoker"/> runs, or something outside it, which an
/// <see cref="IExternalDispatcher"/> reaches. Every call site asks for a binding and invokes it, so none of
/// them carries that decision — and a new kind of callable target is a case here rather than a branch in
/// each of them.
/// <para>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </para>
/// </remarks>
public interface ICallableBindingFactory
{
    /// <summary>
    /// The binding that invokes <paramref name="member"/>.
    /// </summary>
    /// <param name="member">The member to bind.</param>
    /// <param name="receiver">
    /// The object a member of a class is bound to — the <c>Me</c> of the call — or <see langword="null"/> for
    /// a member of a module.
    /// </param>
    /// <param name="callSite">
    /// Where the call is written, for a member that cannot know it for itself. A workspace procedure locates
    /// its own errors from the statement that raised them; an external member has no statements.
    /// </param>
    ICallableBinding ForMember(VBTypeMemberSymbol member, IRuntimeValue? receiver = null, SourceLocation callSite = default);
}
