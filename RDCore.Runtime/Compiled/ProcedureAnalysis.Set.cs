using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a Set statement.
internal sealed partial class ProcedureAnalysis
{
    // MS-VBAL §5.4.3.9: a Set compiled code assigns (SetStep), or else the interpreter's statement, run as a support call (SupportCallStep) - what is
    // given is a call, a member of an object, New, or anything else the interpreter evaluates, or what is assigned is a WithEvents variable, a parameter
    // passed by reference, or anything else the interpreter assigns. A Property Set of an object is called by compiled code (AccessorCallOf).
    private CompiledStep? AnalyzeSet(Instruction instruction, AssignmentStatementNode assignment)
    {
        var mark = _reasons.Count;
        if (assignment.Target is MemberAccessExpressionNode && AccessorCallOf(instruction.Offset, assignment, isSet: true) is { } accessor)
        {
            return accessor;
        }

        return SetOf(instruction.Offset, assignment) is { } set ? set : SupportCallOf(instruction, mark);
    }

    // a Set compiled code assigns, as ExecuteSetAssignment assigns: an object variable of the activation - neither Me nor a parameter passed by reference,
    // whose cell is its caller's - or an object field it reaches, or the function result variable of a function returning an object, given an object
    // variable or field, Me, or Nothing. What it is given is Set-coerced to the declared type of what it is assigned to, at run time
    // (CompiledActivation.TrySet). An object field of one of its records is assigned or given in its own cell (FieldSetStep, FieldSourceSetStep). A
    // WithEvents variable has its handlers detached and attached again by the Set, which is the interpreter's. An object variable declared As New is
    // assigned too: a Set does not read the variable, and reading it is what makes its object (MS-VBAL §5.2.3.1.4), so the variable holds what it is given,
    // Nothing included, until it is next read. The default instance of a class is assigned where ObjectSlotOf finds it for a read.
    private CompiledStep? SetOf(int offset, AssignmentStatementNode assignment)
    {
        if (assignment.Target is MemberAccessExpressionNode && ObjectFieldOf(Bind(assignment.Target)) is { } field)
        {
            return AnalyzeFieldSet(offset, assignment, field);
        }

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
            case BoundVariable variable when ObjectSlotOf(variable, receiver: variable.Variable is not VBPredeclaredInstanceSymbol) is { } slot
                && slot != MeSlot && !IsPassedByReference(slot) && !variable.Variable.GetProperty(SymbolProperties.WithEvents):
                target = slot;
                break;
            default:
                Refuse(IneligibilityKind.AssignmentTarget, name.IdentifierName, assignment.SourceLocation);
                return null;
        }

        // an object field of a record is read in its own cell, which a SetStep has no slot for.
        if (ObjectFieldOf(Bind(assignment.Value)) is { } held)
        {
            return new FieldSourceSetStep(offset, target, held);
        }

        // a function, or a function or Property Get member of an object, that returns an object: compiled code makes the call (ObjectCallOf), and what it
        // returned, held by the statement's temporaries, is what the Set is given.
        if (_nesting is not null && ObjectCallOf(Bind(assignment.Value)) is { } call)
        {
            return new SetStep(offset, target, Source: null) { SourceCall = call };
        }

        // a New of a class: compiled code makes the object as the interpreter makes it (CompiledActivation.New), the statement's temporary until it is over,
        // and the Set is given it.
        if (_nesting is not null && Bind(assignment.Value) is BoundNew created && KnownOperands.IndexOf(_nesting, created.Expression) is >= 0 and var syntax)
        {
            _news.Add(created);
            return new SetStep(offset, target, Source: null) { SourceNew = new NewNode(_news.Count - 1, syntax) };
        }

        return TrySetSource(Bind(assignment.Value), out var source) ? new SetStep(offset, target, source) : null;
    }

    /// <summary>
    /// The <c>New</c> expressions of the body's <c>Set</c> statements whose objects compiled code makes, by the number compiled code knows each by
    /// (<see cref="NewNode.Made"/>).
    /// </summary>
    public IReadOnlyList<BoundNew> News => _news;

    private readonly List<BoundNew> _news = [];

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
/// <see cref="Source"/>, or to what <see cref="SourceCall"/> returns, or to the object <see cref="SourceNew"/> makes, or to <c>Nothing</c> when it has
/// none of them.
/// </summary>
internal sealed record class SetStep(int Offset, int Target, int? Source) : CompiledStep(Offset)
{
    /// <summary>
    /// The call whose object the <c>Set</c> is given: made first, and what it returns held by the statement's temporaries until the statement is over.
    /// </summary>
    public ObjectCallNode? SourceCall { get; init; }

    /// <summary>
    /// The <c>New</c> whose object the <c>Set</c> is given: made first, and held by the statement's temporaries until the statement is over.
    /// </summary>
    public NewNode? SourceNew { get; init; }

    /// <summary>
    /// The <see cref="Target"/> of a <c>Set</c> of the function result variable.
    /// </summary>
    public const int Result = -1;
}
