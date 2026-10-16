using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Plans;

namespace RDCore.SDK.Semantics.Binding;

/// <summary>
/// A node of the bound tree (IR lowering §3.1): an expression, with what the static semantics decided of it once - what a name refers to, the plan of an
/// operation - so that evaluating it does not decide it again.
/// </summary>
/// <remarks>
/// <para>
/// A bound node keeps the expression it was bound from: its <c>Identity</c> and <c>Location</c> are what an error, a fact and the debugger are keyed by, as
/// they are when the expression is evaluated from the syntax tree.
/// </para>
/// <para>
/// A binding is true of the scopes it was made in (<see cref="ExpressionBinder.Bind"/>): a name is bound to the symbol it resolves to in them, which a symbol
/// defined later can change. Whoever keeps a bound tree keeps the scopes with it, and binds the expression again when they are not the session's.
/// </para>
/// </remarks>
/// <param name="Expression">The expression this node was bound from.</param>
public abstract record class BoundNode(ExpressionNode Expression);

/// <summary>
/// A literal (<strong>MS-VBAL §5.6.5</strong>).
/// </summary>
/// <param name="Literal">The literal expression.</param>
public sealed record class BoundLiteral(LiteralExpressionNode Literal) : BoundNode(Literal);

/// <summary>
/// A simple name (<strong>MS-VBAL §5.6.10</strong>), and the symbol it resolves to.
/// </summary>
/// <remarks>
/// What the name evaluates to - a variable's value, a constant's, a call of a procedure that takes no argument - is the symbol's to say: the binding only
/// spares the evaluation the resolution of the name.
/// </remarks>
/// <param name="Name">The simple name expression.</param>
/// <param name="Symbol">The symbol the name resolves to, from the scope it is written in.</param>
public sealed record class BoundName(SimpleNameExpressionNode Name, Symbol Symbol) : BoundNode(Name);

/// <summary>
/// A binary operation (<strong>MS-VBAL §5.6.9</strong>), its operands, and its plan.
/// </summary>
/// <param name="Operation">The binary operator expression.</param>
/// <param name="Plan">The plan of the operation, for the declared types of its operands: deferred when one is a Variant or an object
/// (<see cref="OperationPlan.IsDeferred"/>).</param>
/// <param name="Left">The left operand.</param>
/// <param name="Right">The right operand.</param>
public sealed record class BoundBinary(VBBinaryOperatorExpressionNode Operation, OperationPlan Plan, BoundNode Left, BoundNode Right) : BoundNode(Operation);

/// <summary>
/// A unary operation (<strong>MS-VBAL §5.6.9</strong>), or a pair of parentheses (<strong>§5.6.6</strong>), and its operand.
/// </summary>
/// <param name="Operation">The unary operator expression.</param>
/// <param name="Plan">The plan of the operation, for the declared type of its operand; <see langword="null"/> for parentheses, which are a conversion and
/// have no table of effective types.</param>
/// <param name="Operand">The operand.</param>
public sealed record class BoundUnary(VBUnaryOperatorExpressionNode Operation, OperationPlan? Plan, BoundNode Operand) : BoundNode(Operation);

/// <summary>
/// An expression the binder does not bind yet (IR lowering D3): it is evaluated from the syntax tree, the way it always was.
/// </summary>
/// <remarks>
/// 🚧 Calls, members, indexes and the special forms bind in later slices, which shrink this to nothing (S0.12). A compile error is not bound either: the
/// static pass reports it, and the evaluation of the syntax tree is what raises its run-time counterpart.
/// </remarks>
/// <param name="Expression">The expression.</param>
public sealed record class BoundFallback(ExpressionNode Expression) : BoundNode(Expression);
