using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.Runtime.LanguageCore.Plans;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Binding;

// decided once by the static semantics, so evaluating it does not decide it again.
// a binding holds for the scopes it was made in: whoever keeps a bound tree keeps the scopes, and binds again when they are not the session's.
public abstract record class BoundNode(ExpressionNode Expression);

// MS-VBAL §5.6.5
public sealed record class BoundLiteral(LiteralExpressionNode Literal) : BoundNode(Literal);

// MS-VBAL §5.6.10
public sealed record class BoundName(SimpleNameExpressionNode Name, Symbol Symbol) : BoundNode(Name);

// MS-VBAL §5.6.10, §5.2.3.1.4
public sealed record class BoundVariable(SimpleNameExpressionNode Name, Symbol Variable, FrameSlot? Slot, bool IsAutoInstantiated) : BoundNode(Name);

// MS-VBAL §5.6.11
public sealed record class BoundInstance(InstanceExpressionNode Instance, Symbol Me, FrameSlot? Slot) : BoundNode(Instance);

// MS-VBAL §5.6.9
public sealed record class BoundBinary(VBBinaryOperatorExpressionNode Operation, OperationPlan? Plan, BoundNode Left, BoundNode Right) : BoundNode(Operation);

// MS-VBAL §5.6.9, §5.6.6, §5.6.9.3.1, §5.6.16.1
// MS-VBA evaluates a negated constant expression as it compiles: the least Long is negated as the Long it is, where any other negation of it overflows.
public sealed record class BoundUnary(VBUnaryOperatorExpressionNode Operation, OperationPlan? Plan, BoundNode Operand, bool NegatesAConstantExpression = false)
    : BoundNode(Operation);

// MS-VBAL §5.6.13, §5.6.12
public sealed record class BoundCall(IndexExpressionNode Call, Symbol Procedure, ImmutableArray<BoundArgument>? BoundArguments = null) : BoundNode(Call)
{
    public VBType? ResultType { get; init; }

    // MS-VBAL §5.6.16.1
    // MS-VBA evaluates it as it compiles: Abs of the least Long in one is that Long, where Abs of a variable holding it overflows.
    public bool IsAConstantExpression { get; init; }
}

// MS-VBAL §5.6.12
public sealed record class BoundQualifiedName(MemberAccessExpressionNode Access, Symbol Member) : BoundNode(Access);

// MS-VBAL §3.4
public sealed record class BoundPrecompilerName(PrecompilerNameExpressionNode Name) : BoundNode(Name);

// MS-VBAL §5.6.12, §5.6.13, §5.3.1.9, §5.6.15
public sealed record class BoundMember(
    ExpressionNode Expression, MemberAccessExpressionNode Access, BoundNode? Owner, VBClassModuleSymbol? DeclaredClass, VBTypeMemberSymbol? Member,
    IndexExpressionNode? Arguments, ImmutableArray<BoundArgument>? BoundArguments = null) : BoundNode(Expression)
{
    // MS-VBAL §5.6.12
    // what a late-bound member returns is a Variant whatever it declares: an Integer property read that way widens where an Integer overflows.
    public bool IsLateBound { get; init; }
}

// MS-VBAL §5.6.10, §5.2.3.1.4
public sealed record class BoundNew(NewExpressionNode New, VBClassModuleSymbol Class) : BoundNode(New);

// MS-VBAL §5.6.13
public sealed record class BoundIndex(IndexExpressionNode Index, BoundNode Callee, ImmutableArray<BoundNode> Arguments) : BoundNode(Index);

// MS-VBAL §5.6.9.9
public sealed record class BoundTypeOf(TypeOfIsExpressionNode TypeOf, BoundNode Operand, VBClassModuleSymbol Class) : BoundNode(TypeOf);

// MS-VBAL §3.3.5.2
public sealed record class BoundArrayBound(ArrayBoundExpressionNode ArrayBound, BoundNode Array, BoundNode? Dimension) : BoundNode(ArrayBound);

// MS-VBAL §3.3.5.2
public sealed record class BoundArray(ArrayExpressionNode Array, ImmutableArray<BoundNode> Elements) : BoundNode(Array);

// MS-VBAL §5.6.14
public sealed record class BoundDictionaryAccess(DictionaryAccessExpressionNode DictionaryAccess, BoundNode? Owner) : BoundNode(DictionaryAccess);

// MS-VBAL §5.6.10
public sealed record class BoundLate(ExpressionNode Expression) : BoundNode(Expression);

// RD-VBAL §2.6.2
public sealed record class BoundError(ExpressionNode Expression, VBCompileErrorInfo Error, BoundNode Late) : BoundNode(Expression);
