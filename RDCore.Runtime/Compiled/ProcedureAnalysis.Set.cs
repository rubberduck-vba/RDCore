using RDCore.Runtime.Execution;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Binding;
using RDCore.SDK.Semantics.Instructions;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a Set statement.
internal sealed partial class ProcedureAnalysis
{
    // MS-VBAL §5.4.3.9: a Set compiled code assigns (SetStep), or else the interpreter's statement, run as a support call (SupportCallStep) - what is
    // given is a call, a member of an object, New, or anything else the interpreter evaluates, or what is assigned is a WithEvents variable, a parameter
    // passed by reference, or anything else the interpreter assigns.
    private CompiledStep? AnalyzeSet(Instruction instruction, AssignmentStatementNode assignment)
    {
        var mark = _reasons.Count;
        return SetOf(instruction.Offset, assignment) is { } set ? set : SupportCallOf(instruction, mark);
    }

    // a Set compiled code assigns, as ExecuteSetAssignment assigns: an object variable of the activation - neither Me nor a parameter passed by reference,
    // whose cell is its caller's - or an object field it reaches, or the function result variable of a function returning an object, given an object
    // variable or field, Me, or Nothing. What it is given is Set-coerced to the declared type of what it is assigned to, at run time
    // (CompiledActivation.TrySet). A WithEvents variable has its handlers detached and attached again by the Set, which is the interpreter's.
    private SetStep? SetOf(int offset, AssignmentStatementNode assignment)
    {
        if (assignment.Target is not SimpleNameExpressionNode name)
        {
            Refuse(IneligibilityKind.AssignmentTarget, assignment.Target.GetType().Name, assignment.SourceLocation);
            return null;
        }

        int target;
        switch (Bind(assignment.Target))
        {
            case BoundName { Symbol: var function } when IsOwnFunction(function) && _procedure is VBReturningMemberSymbol { ResolvedType: var returned }
                && IsObjectType(returned):
                target = SetStep.Result;
                break;
            case BoundVariable variable when ObjectSlotOf(variable) is { } slot && slot != MeSlot && !IsPassedByReference(slot)
                && !variable.Variable.GetProperty(SymbolProperties.WithEvents):
                target = slot;
                break;
            default:
                Refuse(IneligibilityKind.AssignmentTarget, name.IdentifierName, assignment.SourceLocation);
                return null;
        }

        return TrySetSource(Bind(assignment.Value), out var source) ? new SetStep(offset, target, source) : null;
    }

    private bool IsPassedByReference(int slot) => _slotSymbols[slot] is VBParameterSymbol { ParameterKind: var kind } && RuntimeProcedureInvoker.IsByRef(kind);

    // what a Set is given that compiled code reads: an object variable of the activation or an object field it reaches, Me, or Nothing.
    private bool TrySetSource(BoundNode value, out int? slot)
    {
        slot = null;
        switch (value)
        {
            case BoundVariable variable when ObjectSlotOf(variable) is { } held:
                slot = held;
                return true;
            case BoundInstance { Slot: { } me } when MeSlot is { } meSlot && ReferenceEquals(me.Layout, _layout) && me.Index == meSlot:
                slot = meSlot;
                return true;
            case BoundLiteral { Literal.StaticValue: VBObjectValue nothing } when nothing.IsNothing():
                return true;
            default:
                _ = Unsupported(value);
                return false;
        }
    }
}

/// <summary>
/// A <c>Set</c> of <see cref="Target"/> (an object slot, or the function result variable, <see cref="Result"/>) to the object held in the slot
/// <see cref="Source"/>, or to <c>Nothing</c> when it has none.
/// </summary>
internal sealed record class SetStep(int Offset, int Target, int? Source) : CompiledStep(Offset)
{
    /// <summary>
    /// The <see cref="Target"/> of a <c>Set</c> of the function result variable.
    /// </summary>
    public const int Result = -1;
}
