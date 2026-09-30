using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Context;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Base for the let-coercion operator (MS-VBAL §5.6.6) runtime-semantics tests: drives the operator
/// through its public <c>Evaluate</c> entry point.
/// </summary>
/// <remarks>
/// The operator is unary — a pair of parentheses around one expression. It used to be modelled as a
/// binary one taking the coercion target as a right-hand operand, which is a shape the language has no
/// operator for: <c>OperatorSymbolNames</c> names <c>__c()_op</c> and nothing binary, and nothing in
/// production ever dispatched to the binary form. Coercing a value to some <em>other</em> named type is
/// what <c>ILetCoercionRuntimeSemanticsProvider</c> does directly, and the matrix for that lives on the
/// provider's own suites.
/// </remarks>
public abstract class OperatorLetCoerceRuntimeSemanticsTests : OperatorArithmeticRuntimeSemanticsTests
{
    // Identity must be a real SyntaxNodeId (see OperatorConcatRuntimeSemanticsTests for why): the real
    // coercion provider's recursion guard hashes LetCoercionStackFrame, which hashes this Identity.
    private static readonly VBUnaryOperatorExpressionNode ThrowawayUnary = new(
        OperatorSymbolNames.UnaryLetCoerceOp, NodeId, TestLocations.TestLocation,
        [new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBLongValue(0))]);

    protected static RuntimeSemanticsEvaluationResult Evaluate(
        UnaryLetCoerceOperatorRuntimeSemantics op, VBTypedValue source)
        => op.Evaluate(FakeSession(), new ConversionOperationSemanticContext(), ThrowawayUnary, source);

    /// <summary>Runs step 1 of the operator pipeline: resolves the effective type from the operand.</summary>
    protected static DetermineOperatorEffectiveTypeResult DetermineEffectiveType(
        UnaryLetCoerceOperatorRuntimeSemantics op, VBTypedValue source)
    {
        var frame = new OperatorEvaluationFrame(NodeId, [source], VBUnknownType.TypeInfo);
        return op.DetermineOperatorEffectiveType(null!, new ConversionOperationSemanticContext(), ThrowawayUnary, frame);
    }
}
