using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
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
/// A simple name that is a variable: a parameter, a local or <c>Static</c> variable, or a field (<strong>MS-VBAL §5.6.10</strong>), whose value is read.
/// </summary>
/// <remarks>
/// What reading a name is - a call, a constant, an enumeration member's value, or a variable's value - depends on the symbol alone, and is decided once,
/// here. A variable of the procedure the name is written in has its <see cref="Slot"/> in the procedure's <see cref="FrameLayout"/>.
/// </remarks>
/// <param name="Name">The simple name expression.</param>
/// <param name="Variable">The variable the name resolves to.</param>
/// <param name="Slot">The slot of the variable, when it is a parameter or a variable of the procedure the name is written in, and is not <c>Static</c>.</param>
/// <param name="IsAutoInstantiated">Whether the variable is declared <c>As New</c> (<strong>MS-VBAL §5.2.3.1.4</strong>): a reference to it creates the
/// object it does not hold yet.</param>
public sealed record class BoundVariable(SimpleNameExpressionNode Name, Symbol Variable, FrameSlot? Slot, bool IsAutoInstantiated) : BoundNode(Name);

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
/// A call of a procedure the name before its argument list resolves to (<strong>MS-VBAL §5.6.13</strong>): a <c>Sub</c>, a <c>Function</c> or a
/// <c>Property Get</c> of the workspace or of a library, by its bare name.
/// </summary>
/// <remarks>
/// The binding is the resolution of the callee, made once. How the arguments are passed - aliased to a variable, let-coerced, collected into a
/// <c>ParamArray</c>, omitted - is the call's to decide each time it is made, from the arguments as they are written; each argument is an expression of its
/// own, bound in its turn. 🚧 Binding how each argument is passed is a later step.
/// </remarks>
/// <param name="Call">The index expression that is the call.</param>
/// <param name="Procedure">The procedure called.</param>
public sealed record class BoundCall(IndexExpressionNode Call, Symbol Procedure) : BoundNode(Call);

/// <summary>
/// A member of an object whose declared type is a class (<strong>MS-VBAL §5.6.12</strong>), read - <c>c.Value</c> - or called with the arguments written
/// after it (<strong>§5.6.13</strong>) - <c>c.Add 1</c>, <c>c.Item(1)</c>: a <c>Sub</c>, a <c>Function</c> or a <c>Property Get</c> of the class.
/// </summary>
/// <remarks>
/// The binding is the resolution of the member in the class the owner is declared as, made once. The object the owner holds at run time is an instance of
/// that class, or of a class that implements it as an interface (<strong>§5.3.1.9</strong>), whose member is then the one implementing it: which one is a
/// fact of the object's class, decided once for each class the expression meets. A member that is a public variable of the class, a member of an object
/// declared <c>Object</c> or <c>Variant</c> (late-bound) is not bound yet.
/// </remarks>
/// <param name="Expression">The expression: the member access, or the index expression that calls it.</param>
/// <param name="Access">The member access.</param>
/// <param name="Owner">The object the member is accessed on, bound; <see langword="null"/> for a member written with no object before the dot, which is a
/// member of the target of the enclosing <c>With</c> block (<strong>MS-VBAL §5.6.15</strong>).</param>
/// <param name="DeclaredClass">The class the owner is declared as.</param>
/// <param name="Member">The member of <paramref name="DeclaredClass"/> the name resolves to.</param>
/// <param name="Arguments">The arguments of a call; <see langword="null"/> for a member that is read, with no argument list.</param>
public sealed record class BoundMember(
    ExpressionNode Expression, MemberAccessExpressionNode Access, BoundNode? Owner, VBClassModuleSymbol DeclaredClass, VBTypeMemberSymbol Member,
    IndexExpressionNode? Arguments) : BoundNode(Expression);

/// <summary>
/// A <c>New</c> expression (<strong>MS-VBAL §5.6.10</strong>'s <c>New</c> keyword, <strong>§5.2.3.1.4</strong>), and the class it creates an object of.
/// </summary>
/// <remarks>
/// The binding is the resolution of the class the type expression names, made once; creating the object and running its <c>Initialize</c> is the
/// evaluation's, each time.
/// </remarks>
/// <param name="New">The <c>New</c> expression.</param>
/// <param name="Class">The class module of the class created.</param>
public sealed record class BoundNew(NewExpressionNode New, VBClassModuleSymbol Class) : BoundNode(New);

/// <summary>
/// An expression the binder does not bind yet (IR lowering D3): it is evaluated from the syntax tree, the way it always was.
/// </summary>
/// <remarks>
/// 🚧 Late-bound members, indexes and the special forms bind in later slices, which shrink this to nothing (S0.12). A compile error is not bound either: the
/// static pass reports it, and the evaluation of the syntax tree is what raises its run-time counterpart.
/// </remarks>
/// <param name="Expression">The expression.</param>
public sealed record class BoundFallback(ExpressionNode Expression) : BoundNode(Expression);
