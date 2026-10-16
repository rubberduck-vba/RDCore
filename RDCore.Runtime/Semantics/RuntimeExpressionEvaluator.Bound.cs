using RDCore.Runtime.Semantics.Literals;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Binding;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Semantics;

// The evaluation of bound trees (IR lowering §3.3, S0.8a): what an expression binds to is decided once, the first time it is evaluated, and the bound tree
// is what is evaluated from then on. A node is evaluated by the same semantics that evaluate it from the syntax tree - only what the binding decided is not
// decided again.
public sealed partial class RuntimeExpressionEvaluator
{
    // a bound tree, and the scopes it was bound in: what a name is bound to is true of these scopes, and of no other.
    private sealed record class BoundRoot(ScopeTree Scopes, BoundNode Node);

    // keyed by the expression itself, which a module that is loaded again parses anew: the tree of a module that is gone goes with it.
    private readonly ConditionalWeakTable<ExpressionNode, BoundRoot> _boundRoots = [];

    /// <summary>
    /// Whether every expression that has no effect of its own is evaluated twice - from its bound tree and from its syntax tree - and the two results
    /// compared, an <see cref="InvalidOperationException"/> being thrown when they differ (IR lowering S0.8a).
    /// </summary>
    /// <remarks>
    /// 🧪 For the tests only, which turn it on for the whole suite. An expression has an effect when it calls something, an object's default member
    /// included, or when it is evaluated by an analysis, which counts what it assumes: those are evaluated once.
    /// </remarks>
    internal static bool ComparesBoundEvaluation { get; set; }

    // the bound tree of an expression, bound now when it is not bound yet, or was bound in scopes that are no longer the session's.
    private BoundNode BoundTreeOf(IRuntimeSession session, ExpressionNode expression, RuntimeEvaluationContext context)
    {
        var scopes = session.Symbols.Scopes;
        if (_boundRoots.TryGetValue(expression, out var root) && ReferenceEquals(root.Scopes, scopes))
        {
            return root.Node;
        }

        var node = Bind(session, scopes, expression, context);
        _boundRoots.AddOrUpdate(expression, new BoundRoot(scopes, node));
        return node;
    }

    // the binding is the static evaluation of the expression, in the scope the evaluation resolves its names from. An expression with no such scope - one
    // that is not written in code the session defines - is not bound.
    private static BoundNode Bind(IRuntimeSession session, ScopeTree scopes, ExpressionNode expression, RuntimeEvaluationContext context)
        => scopes.TryGetScope(context.Scope, out var scope)
            ? ExpressionBinder.Bind(new StaticEvaluationContext(session.Symbols.Resolver, scope), expression).NodeOf(expression) ?? new BoundFallback(expression)
            : new BoundFallback(expression);

    private RuntimeSemanticsEvaluationResult EvaluateBound(IRuntimeSession session, BoundNode node, RuntimeEvaluationContext context)
    {
        var result = EvaluateBoundNode(session, node, context);
        if (ComparesBoundEvaluation && !AssumesVariables && Observation is null && HasNoEffect(node))
        {
            var expected = EvaluateSyntax(session, node.Expression, context);
            if (Outcome(result) != Outcome(expected))
            {
                throw new InvalidOperationException(
                    $"The bound tree of the {node.Expression.GetType().Name} at {node.Expression.Location.Range} evaluated to {Outcome(result)}, and its syntax tree to {Outcome(expected)}.");
            }
        }

        return result;
    }

    private RuntimeSemanticsEvaluationResult EvaluateBoundNode(IRuntimeSession session, BoundNode node, RuntimeEvaluationContext context) => node switch
    {
        BoundLiteral literal => LiteralExpressionRuntimeSemantics.Instance.Evaluate(session, new(), literal.Literal),
        BoundName name => ReadSymbol(session, context, name.Symbol, nameOfEnclosingFunctionIsItsResult: true),
        BoundBinary binary => EvaluateBoundBinary(session, binary, context),
        BoundUnary unary => EvaluateBoundUnary(session, unary, context),
        _ => EvaluateSyntax(session, node.Expression, context),
    };

    private RuntimeSemanticsEvaluationResult EvaluateBoundBinary(IRuntimeSession session, BoundBinary binary, RuntimeEvaluationContext context)
    {
        var leftResult = EvaluateBoundNode(session, binary.Left, context);
        if (!leftResult.IsSuccess)
        {
            return leftResult;
        }

        var rightResult = EvaluateBoundNode(session, binary.Right, context);
        if (!rightResult.IsSuccess)
        {
            return rightResult;
        }

        return OperatorProvider.EvaluateBinaryOperator(session, binary.Operation, leftResult.Result!, rightResult.Result!);
    }

    private RuntimeSemanticsEvaluationResult EvaluateBoundUnary(IRuntimeSession session, BoundUnary unary, RuntimeEvaluationContext context)
    {
        var operandResult = EvaluateBoundNode(session, unary.Operand, context);
        return operandResult.IsSuccess
            ? OperatorProvider.EvaluateUnaryOperator(session, unary.Operation, operandResult.Result!)
            : operandResult;
    }

    // a literal, and a variable or a constant of an intrinsic type, have none; nor does an operation of them, whose operands are no object with a default
    // member to call.
    private static bool HasNoEffect(BoundNode node) => node switch
    {
        BoundLiteral => true,
        BoundName { Symbol: ITypedSymbol { ResolvedType: VBNumericType or VBStringType or VBBooleanType or VBDateType } and not VBTypeMemberSymbol } => true,
        BoundName { Symbol: VBTypeMemberSymbol { Kind: SymbolKindExt.Field or SymbolKindExt.Variable or SymbolKindExt.Constant } member }
            => member is ITypedSymbol { ResolvedType: VBNumericType or VBStringType or VBBooleanType or VBDateType },
        BoundBinary binary => HasNoEffect(binary.Left) && HasNoEffect(binary.Right),
        BoundUnary unary => HasNoEffect(unary.Operand),
        _ => false,
    };

    private static string Outcome(RuntimeSemanticsEvaluationResult result)
        => result.IsSuccess ? $"{result.Result!.TypeInfo.Name}({result.Result.RuntimeValue.BoxedValue})" : $"error {result.ErrorInfo?.ErrorId}";
}
