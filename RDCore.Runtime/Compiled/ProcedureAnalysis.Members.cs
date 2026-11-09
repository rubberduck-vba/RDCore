using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.SDK.Semantics.Instructions;

namespace RDCore.Runtime.Compiled;

/// <remarks>
/// A <c>Let</c> of a member of an object declared as a class (an object variable compiled code holds, <c>Me</c>, or the object of the enclosing
/// <c>With</c> block) is assigned as the interpreter assigns it (<c>CompiledActivation.LetMember</c>): the value is evaluated by compiled code, and the
/// member found on the object and assigned by the interpreter's own assignment, a field or a <c>Property Let</c>. A member of the object of a <c>With</c>
/// block is called as a member of an object variable is, the object the one the interpreter holds for the block: the <c>With</c> statement and its
/// <c>End With</c> are the interpreter's, which evaluates, holds and lets go of it.
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

        return Dynamic(assigned) is { } value ? new MemberLetStep(offset, value) : null;
    }
}

/// <summary>
/// A <c>Let</c> of a member of an object (MS-VBAL §5.4.3.8): <see cref="Value"/> evaluated by compiled code, then assigned by the interpreter's own
/// assignment as a support call (<c>CompiledActivation.LetMember</c>).
/// </summary>
internal sealed record class MemberLetStep(int Offset, VariantNode Value) : CompiledStep(Offset);
