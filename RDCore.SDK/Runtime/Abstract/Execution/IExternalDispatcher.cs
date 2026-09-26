using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// One call to something that is not the workspace's own code: a standard-library member, a referenced
/// library's, a host application's object model, or a native export a <c>Declare</c> named.
/// </summary>
/// <remarks>
/// A growable record rather than a parameter list, so a new fact about a call — the policy decision that
/// let it through, the reference it came from — gets a field here rather than a new parameter on every
/// dispatcher.
/// </remarks>
/// <param name="Member">
/// The member being called. Its <see cref="SymbolProperties.ExternalTarget"/> is what says which
/// implementation to reach, and its own parameters say how to marshal the arguments.
/// </param>
/// <param name="Arguments">
/// The arguments, in the member's declared parameter order, <em>including</em> the receiver at index
/// <c>0</c> for a member of a class — the same shape <see cref="IProcedureInvoker.Invoke"/> takes, so a
/// call site does not have to know which of the two will run it.
/// </param>
/// <param name="CallSite">
/// Where the call is written. An external member that raises an error cannot know this for itself, and an
/// error with no location is one no editor can point at.
/// </param>
public sealed record class ExternalCallRequest(
    VBTypeMemberSymbol Member,
    IRuntimeValue[] Arguments,
    SourceLocation CallSite = default)
{
    /// <summary>
    /// Whether this call is to a native library a <c>Declare</c> named — the calls an administrator most
    /// wants a say over, since a library call is a way to run arbitrary code.
    /// </summary>
    public bool IsLibraryImport => Member is VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol;

    /// <summary>
    /// The library a <c>Declare</c> named, or <see langword="null"/> for a call that is not one.
    /// </summary>
    public string? Library => Member switch
    {
        VBExternalFunctionMemberSymbol function => function.Lib,
        VBExternalSubMemberSymbol procedure => procedure.Lib,
        _ => null,
    };

    /// <summary>
    /// The call as one line, arguments included: what an interceptor logs, and what the verbose half of a
    /// refusal has to say so that a reader can tell which call was refused.
    /// </summary>
    /// <remarks>
    /// Names the library and the entry point a <c>Declare</c> actually reaches — its <c>Alias</c> when it has
    /// one, since that is the exported name and the VBA name may be nothing like it.
    /// </remarks>
    public string Describe()
    {
        var entryPoint = Member switch
        {
            VBExternalFunctionMemberSymbol { Alias: { Length: > 0 } alias } => alias,
            VBExternalSubMemberSymbol { Alias: { Length: > 0 } alias } => alias,
            _ => Member.Name,
        };

        var qualified = Library is { Length: > 0 } library ? $"{library}!{entryPoint}" : entryPoint;
        return $"{qualified}({string.Join(", ", Arguments.Select(Format))})";
    }

    // an argument as a reader of a log needs to see it: a string quoted so an empty one is visible, anything
    // else in the invariant culture so the record does not depend on where it was written.
    private static string Format(IRuntimeValue argument) => argument.BoxedValue switch
    {
        null => "Nothing",
        string text => $"\"{text}\"",
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        var other => other.ToString() ?? string.Empty,
    };
}

/// <summary>
/// Runs a call to something outside the workspace.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="IProcedureInvoker"/>, which runs the workspace's own procedures. The two
/// are chosen between by one fact: a workspace procedure has an instruction list, an external member has a
/// <see cref="SymbolProperties.ExternalTarget"/>. Nothing above that point — the resolver, the expression
/// evaluator, the executor, the semantics — is aware of the difference, which is the whole reason this is
/// shaped like the invoker it sits beside.
/// <para>
/// 🎯 Implementations are expected to be composed rather than singular: an availability check, then the
/// workspace's own policy, then whatever is intercepting calls (a log, a mock), and only then the provider
/// that really runs it. A call that is refused, or answered by an interceptor, never reaches a provider —
/// which is what makes a <c>Declare</c> blockable and a <c>MsgBox</c> mockable without either of them
/// knowing.
/// </para>
/// <para>
/// ⚖️<strong>RDCore</strong> provides implementations of this interface <strong>licensed under GPLv3</strong>.
/// </para>
/// </remarks>
public interface IExternalDispatcher
{
    /// <summary>
    /// Runs <paramref name="request"/> and returns what it yielded, the way the semantics of the language
    /// do: an error is in the result rather than thrown.
    /// </summary>
    /// <remarks>
    /// A member this dispatcher cannot reach — no implementation, no provider, or a policy that refuses it
    /// — is a <em>run-time error</em> saying so, not an internal error and never a silent success. A
    /// workspace can trap it like any other.
    /// </remarks>
    /// <param name="request">The call to run.</param>
    /// <param name="resolver">A read-only interface over the current execution context.</param>
    RuntimeSemanticsEvaluationResult Invoke(ExternalCallRequest request, ISymbolResolver resolver);
}
