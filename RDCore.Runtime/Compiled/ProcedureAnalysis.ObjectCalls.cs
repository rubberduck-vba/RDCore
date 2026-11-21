using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call that returns an object, inside an expression: the object a member is called on, or a public variable is read of
// (`CurrentShip.GridPosition.Y`, `Make().Pos.Move 1`), as the interpreter evaluates the member's owner before the member (EvaluateBoundMember).
internal sealed partial class ProcedureAnalysis
{
    // whether the instruction is translated again with no call made for an object, the interpreter making each as it did (AnalyzeInstruction).
    private bool _withoutObjectCalls;

    // a function of a standard module, or a function or Property Get member of an object, that returns an object, called in an instruction that calls inside
    // an expression (NestingOf), by where it is in its statement: what it returns is the statement's temporary, which the interpreter holds as it holds what a
    // call it makes returns (RuntimeProcedureInvoker.ReleaseLocals), and is let go of when the statement is over.
    private ObjectCallNode? ObjectCallOf(BoundNode node)
    {
        if (_nesting is null || _withoutObjectCalls
            || node is not (BoundCall or BoundMember { Member: VBFunctionMemberSymbol or VBPropertyGetMemberSymbol } or BoundName { Symbol: VBFunctionMemberSymbol or VBPropertyGetMemberSymbol })
            || KnownOperands.IndexOf(_nesting, node.Expression) is not (>= 0 and var syntax))
        {
            return null;
        }

        var made = node switch
        {
            BoundCall call => CallOf(call, CallStep.NoResult, -1, resultKind: null, returnsObject: true),
            BoundMember member => MemberCallOf(member, CallStep.NoResult, -1, resultKind: null, returnsObject: true),
            // a function, or a member of the class whose code runs, named by itself (BareCallOf); not the function's own result variable.
            BoundName { Symbol: VBTypeMemberSymbol named } when !IsOwnFunction(named)
                => BareCallOf(named, -1, kind: null, node.Expression, returnsObject: true),
            _ => null,
        };

        return made is null ? null : new ObjectCallNode(made.Callee, made.Arguments, made.Receiver, syntax)
        {
            WithAt = made.WithAt, ReceiverField = made.ReceiverField, ReceiverCall = made.ReceiverCall,
        };
    }

    /// <summary>
    /// Whether <paramref name="step"/> calls a function or a <c>Property Get</c> that returns an object (<see cref="ObjectCallNode"/>), or a function that
    /// returns a String or a Variant given to the standard library (<see cref="CallValueNode"/>), or makes an object (<see cref="NewNode"/>): the statement
    /// is begun before its first call, and ended once it is over (<see cref="CompiledActivation.BeginStatement"/>,
    /// <see cref="CompiledActivation.EndStatement"/>). A call of a member of what a call returns, which returns a <c>String</c>
    /// (<c>Make("m").Hello</c>), is one: the object <c>Make</c> returns is held until the statement is over.
    /// </summary>
    /// <param name="step">The step.</param>
    internal static bool CallsForAnObject(CompiledStep step)
        => step is CallStep { ReceiverCall: not null } or SetStep { SourceCall: not null } or SetStep { SourceNew: not null }
            or WithStep { SourceCall: not null } or WithStep { SourceNew: not null }
        || PayloadsOf(step).SelectMany(Within).Any(node => ReceiverCallsOf(node).Any())
        || TextCallsOf(step).Any(call => call.ReceiverCall is not null)
        || ValueCallsOf(step).Any();

    // the calls that return an object a member is called on, or read of, the outermost first: each one's receiver is the next.
    private static IEnumerable<ObjectCallNode> ObjectCallsOf(ObjectCallNode? call)
    {
        for (; call is not null; call = call.ReceiverCall)
        {
            yield return call;
        }
    }
}
