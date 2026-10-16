using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Plans;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Binding;

/// <summary>
/// What an expression binds to (IR lowering §3.3): its declared type, or the compile error it is, and for an operation the plan of the operation, made for
/// the declared types of its operands.
/// </summary>
/// <param name="Node">The <c>Identity</c> of the expression.</param>
/// <param name="Result">What the static semantics make of the expression.</param>
/// <param name="Plan">
/// The plan of the operation, for an operator expression whose operands have a declared type: final when the declared types decide it, deferred when the value
/// of an operand does. <see langword="null"/> for an expression that is not an operation, and for the parentheses around one (a conversion).
/// </param>
public sealed record class BoundExpression(SyntaxNodeId Node, StaticSemanticsEvaluationResult Result, OperationPlan? Plan)
{
    /// <summary>
    /// The declared type of the expression; <see langword="null"/> when it is a compile error.
    /// </summary>
    public VBType? DeclaredType => Result.Result;

    /// <summary>
    /// The compile error the expression is, if it is one.
    /// </summary>
    public VBCompileErrorInfo? Error => Result.ErrorInfo;
}

/// <summary>
/// What the expressions of a tree bind to, recorded as the static semantics evaluate them: an operand before the expression that has it.
/// </summary>
/// <remarks>
/// The bindings are those of the expressions the static semantics evaluate: an expression after an operand that is a compile error is not evaluated, and is not
/// bound. Like the evaluation it records, a recorder is for one binding at a time: it is not thread-safe.
/// </remarks>
public sealed class ExpressionBindings
{
    private readonly Dictionary<SyntaxNodeId, BoundExpression> _bound = [];
    private readonly Dictionary<SyntaxNodeId, BoundNode> _nodes = [];

    /// <summary>
    /// What each expression bound to, by its <c>Identity</c>.
    /// </summary>
    public IReadOnlyDictionary<SyntaxNodeId, BoundExpression> Bound => _bound;

    /// <summary>
    /// What the expression with this <c>Identity</c> bound to, if it was bound.
    /// </summary>
    public BoundExpression? this[SyntaxNodeId node] => _bound.GetValueOrDefault(node);

    /// <summary>
    /// The bound tree of an expression that was bound (<see cref="BoundNode"/>); <see langword="null"/> for one that was not.
    /// </summary>
    /// <param name="expression">The expression.</param>
    public BoundNode? NodeOf(ExpressionNode expression) => _nodes.GetValueOrDefault(expression.Identity);

    /// <summary>
    /// Records what an expression the static semantics evaluated binds to. An expression that is evaluated again has its binding replaced.
    /// </summary>
    /// <param name="context">The context the expression was evaluated in: the scope its names resolve from.</param>
    /// <param name="expression">The expression.</param>
    /// <param name="result">What the static semantics made of it.</param>
    internal void Record(StaticEvaluationContext context, ExpressionNode expression, StaticSemanticsEvaluationResult result)
    {
        var plan = PlanOf(expression);
        _bound[expression.Identity] = new BoundExpression(expression.Identity, result, plan);
        _nodes[expression.Identity] = NodeFor(context, expression, result, plan);
    }

    // an operand is recorded before the expression that has it, so its node is there to build this one from. An expression that is a compile error, or that
    // is not bound yet, is a fallback - and so is one with an operand that is.
    private BoundNode NodeFor(StaticEvaluationContext context, ExpressionNode expression, StaticSemanticsEvaluationResult result, OperationPlan? plan)
        => result.IsError ? new BoundFallback(expression) : expression switch
        {
            LiteralExpressionNode literal => new BoundLiteral(literal),

            // the name is resolved as the evaluation resolves it, from the same scope: the binding is the resolution, made once.
            SimpleNameExpressionNode name when context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol is { } symbol
                => new BoundName(name, symbol),

            VBBinaryOperatorExpressionNode binary when plan is not null && OperandNode(binary.Left) is { } left && OperandNode(binary.Right) is { } right
                => new BoundBinary(binary, plan, left, right),

            VBUnaryOperatorExpressionNode unary when OperandNode(unary.Operand) is { } operand
                => new BoundUnary(unary, plan, operand),

            _ => new BoundFallback(expression),
        };

    // the node of an operand that is bound to something other than a fallback.
    private BoundNode? OperandNode(ExpressionNode operand) => NodeOf(operand) is { } node and not BoundFallback ? node : null;

    // the plan of an operation, for the declared types of its operands as the operator is given them (an Enum is a Long, a LongPtr what it is an alias of).
    private OperationPlan? PlanOf(ExpressionNode expression) => expression switch
    {
        VBBinaryOperatorExpressionNode binary when DeclaredTypeOf(binary.Left) is { } left && DeclaredTypeOf(binary.Right) is { } right
            => OperationPlanner.ForDeclared(binary.Token,
                ExpressionStaticSemanticsEvaluator.AsOperand(left), ExpressionStaticSemanticsEvaluator.AsOperand(right)),

        // a pair of parentheses is a conversion, not an operation with a table of effective types.
        VBUnaryOperatorExpressionNode unary when unary.Token is Tokens.NegationOp or Tokens.LogicalNotOp && DeclaredTypeOf(unary.Operand) is { } operand
            => OperationPlanner.ForDeclared(unary.Token, ExpressionStaticSemanticsEvaluator.AsOperand(operand)),

        _ => null,
    };

    private VBType? DeclaredTypeOf(ExpressionNode operand) => this[operand.Identity]?.DeclaredType;
}

/// <summary>
/// What an assignment binds to: its target and its value, and the plan of the let-coercion of the value to the target.
/// </summary>
/// <param name="Expressions">What the expressions of the target and of the value bind to.</param>
/// <param name="Conversion">
/// The plan of the let-coercion of the value to the declared type of the target, for a Let assignment whose target and value have one; <see langword="null"/>
/// for a Set assignment, which is not a let-coercion, and when either is a compile error or the target's type is not known.
/// </param>
public sealed record class BoundAssignment(ExpressionBindings Expressions, ConversionPlan? Conversion);

/// <summary>
/// Binds expressions (IR lowering §3.3): each to its declared type, as the static semantics evaluate it, and each operation to its
/// <see cref="OperationPlan"/>, made for the declared types of its operands - final when they decide it, deferred when a value does.
/// </summary>
/// <remarks>
/// The binder is the static evaluation itself, with a recorder for what it evaluates (<see cref="StaticEvaluationContext.Bindings"/>): a declared type is the
/// one the static semantics give the expression, by construction. Nothing consumes the bindings yet: evaluating an operation by its bound plan rather than
/// planning it again for the values it is given is the next step.
/// </remarks>
public static class ExpressionBinder
{
    /// <summary>
    /// What an expression, and each expression in it, binds to.
    /// </summary>
    /// <param name="context">The context the expression is evaluated in. What the evaluation would record of its facts is not recorded: binding is not
    /// analyzing.</param>
    /// <param name="expression">The expression.</param>
    public static ExpressionBindings Bind(StaticEvaluationContext context, ExpressionNode expression)
    {
        var bindings = new ExpressionBindings();
        ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = null, Bindings = bindings }, expression);
        return bindings;
    }

    /// <summary>
    /// What an assignment binds to: the expressions of its target and of its value, and the plan of the let-coercion of the value to the target.
    /// </summary>
    /// <param name="context">The context the assignment is evaluated in.</param>
    /// <param name="assignment">The assignment.</param>
    public static BoundAssignment BindAssignment(StaticEvaluationContext context, AssignmentStatementNode assignment)
    {
        var bindings = new ExpressionBindings();
        var bindingContext = context with { Facts = null, Bindings = bindings };
        var target = ExpressionStaticSemanticsEvaluator.Evaluate(bindingContext, assignment.Target);
        var value = ExpressionStaticSemanticsEvaluator.Evaluate(bindingContext, assignment.Value);

        var conversion = assignment.Kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet
            && target is { IsSuccess: true, Result: { } targetType and not VBUnknownType }
            && value is { IsSuccess: true, Result: { } valueType }
                ? ConversionPlanner.ForDeclared(valueType, targetType)
                : null;

        return new BoundAssignment(bindings, conversion);
    }
}
