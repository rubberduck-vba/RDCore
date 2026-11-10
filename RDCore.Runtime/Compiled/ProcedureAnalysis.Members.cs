using RDCore.Runtime.Execution;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Runtime.Compiled;

/// <remarks>
/// A <c>Let</c> of a member of an object declared as a class (an object variable compiled code holds, <c>Me</c>, or the object of the enclosing
/// <c>With</c> block) is assigned as the interpreter assigns it (<c>CompiledActivation.LetMember</c>): the value is evaluated by compiled code, and the
/// member found on the object and assigned by the interpreter's own assignment, a field or a <c>Property Let</c>. A <c>Property Let</c> or
/// <c>Property Set</c> of an object variable compiled code holds, or of the object of the enclosing <c>With</c> block, is called by compiled code, as a
/// member is (<see cref="AccessorCallOf"/>). A member of the object of a <c>With</c> block is called as a member of an object variable is, the object the
/// one the interpreter holds for the block: the <c>With</c> statement is the interpreter's, which evaluates and holds it.
/// </remarks>
internal sealed partial class ProcedureAnalysis
{
    // the With block of the instruction being analyzed, by the offset of its With statement, and the block's expression; none outside one.
    private int? _enclosingWith;
    private ExpressionNode? _withExpression;

    private void EnterBlockOf(InstructionList list, Instruction instruction)
    {
        _enclosingWith = instruction.EnclosingWith;
        _withExpression = instruction.EnclosingWith is { } opener ? (list.Items[opener].Node as WithStatementNode)?.WithExpression : null;
    }

    // whether a member access is a member of an object compiled code reaches, declared as a class, not late-bound: of an object variable it holds, of Me, or
    // of the object of the enclosing With block, whose expression is declared as a class. A record's field, and a member of anything else, is not.
    private bool IsMemberOfAnObject(BoundNode node)
        => node is BoundMember { IsLateBound: false, Arguments: null } member && member.Owner switch
        {
            null => _withExpression is { } with && _evaluator.DeclaredTypeOf(_session, with, new RuntimeEvaluationContext(_procedure.Uri)) is VBClassType,
            BoundVariable variable => ObjectSlotOf(variable) is not null && variable.Variable is SDK.Model.Symbols.Abstract.ITypedSymbol { ResolvedType: VBClassType },
            BoundInstance => MeSlot is not null,
            _ => false,
        };

    // MS-VBAL §5.4.3.8 with a <member-access-expression> target that is a member of an object (IsMemberOfAnObject), as AssignValue assigns it: the value
    // evaluated, then the object, and the member found on it assigned the value let-coerced - a field - or called with it - a Property Let. What a call
    // returns is assigned so once the call returned (AssignedCallOf); any other value is one compiled code evaluates on payloads, of its own type.
    private CompiledStep? MemberLetOf(int offset, AssignmentStatementNode assignment)
    {
        var assigned = Bind(assignment.Value);
        if (AssignedCallOf(assigned, offset) is { } call)
        {
            return call;
        }

        if (AccessorCallOf(offset, assignment, isSet: false) is { } accessor)
        {
            return accessor;
        }

        return Dynamic(assigned) is { } value ? new MemberLetStep(offset, value) : null;
    }

    // MS-VBAL §5.4.3.8, §5.4.3.9 with a <member-access-expression> target that is a property of an object variable of the activation, or of the object of the
    // enclosing With block, declared as a class: its Property Let, or its Property Set, called by compiled code as InvokeAssignment calls it, with the value
    // as its last argument, given as a value whatever the parameter's passing, and the activation located at its statement while it runs. It is the member
    // of the class the object is declared as: an object of another class, which implements it, is assigned by the interpreter (CompiledActivation.Takes). A
    // property indexed by arguments, and one whose value parameter compiled code passes nothing to, is the interpreter's to assign; the reasons this found
    // are not the statement's.
    private CallStep? AccessorCallOf(int offset, AssignmentStatementNode assignment, bool isSet)
    {
        if (Bind(assignment.Target) is not BoundMember { DeclaredClass: { } declared, IsLateBound: false, Arguments: null } target
            || (target.Owner is null ? _enclosingWith is null : target.Owner is not BoundVariable)
            || AccessorOf(declared, target.Access.Member.IdentifierName, isSet) is not { } accessor
            || RuntimeProcedureInvoker.GetParameters(accessor) is not [{ Name: SDK.Model.Tokens.Me }, var valueParameter]
            || IsObjectType(valueParameter.ResolvedType) != isSet
            || !ConvertsWithoutDeclining(assignment.Value, valueParameter))
        {
            return null;
        }

        int? receiver = null;
        if (target.Owner is BoundVariable owner)
        {
            if (ObjectSlotOf(owner) is not { } slot)
            {
                return null;
            }

            receiver = slot;
        }

        var mark = _reasons.Count;
        var value = new BoundArgument(valueParameter, ArgumentPassing.Value, assignment.Value, Bind(assignment.Value), Plan: null, Elements: []);
        var call = CallWith(accessor, [valueParameter], [value], CallStep.NoResult, offset, resultKind: null, target with { Member = accessor }, receiver,
            assigns: false, at: null);
        if (call is null)
        {
            _reasons.RemoveRange(mark, _reasons.Count - mark);
            return null;
        }

        return receiver is null ? call with { WithAt = _enclosingWith } : call;
    }

    // whether compiled code converts the value to the kind of the parameter without declining, whatever value of its declared type it is, short of one out of
    // the parameter's range, which is an error the interpreter raises (PayloadOperations.Convert): a value of that kind; an integer or a Boolean, to any kind (a LongLong to a Single excepted); a Single or a Double, to a
    // Single, a Double or a Boolean. What it would decline - a floating-point number to an integer type, which the interpreter rounds, a Currency, a Variant,
    // a Date - it hands the interpreter to assign, as any member's (MemberLetStep). An object parameter's value is Set-coerced, and declines nothing.
    private bool ConvertsWithoutDeclining(ExpressionNode value, VBParameterSymbol parameter)
    {
        if (IsObjectType(parameter.ResolvedType))
        {
            return true;
        }

        if (ScalarKinds.Of(parameter.ResolvedType) is not { } to
            || _evaluator.DeclaredTypeOf(_session, value, new RuntimeEvaluationContext(_procedure.Uri)) is not { } declared || ScalarKinds.Of(declared) is not { } from)
        {
            return false;
        }

        return from == to
            || from is ScalarKind.Byte or ScalarKind.Integer or ScalarKind.Long or ScalarKind.Boolean
            || from is ScalarKind.LongLong && to is not ScalarKind.Single
            || from is ScalarKind.Single or ScalarKind.Double && to is ScalarKind.Single or ScalarKind.Double or ScalarKind.Boolean;
    }

    // the Property Let, or the Property Set, of the name, of the class.
    private static VBTypeMemberSymbol? AccessorOf(VBClassModuleSymbol declared, string name, bool isSet)
        => declared.DefaultInterfaceMembers.FirstOrDefault(candidate => (isSet ? candidate is VBPropertySetMemberSymbol : candidate is VBPropertyLetMemberSymbol)
            && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A <c>Let</c> of a member of an object (MS-VBAL §5.4.3.8): <see cref="Value"/> evaluated by compiled code, then assigned by the interpreter's own
/// assignment as a support call (<c>CompiledActivation.LetMember</c>).
/// </summary>
internal sealed record class MemberLetStep(int Offset, VariantNode Value) : CompiledStep(Offset);
