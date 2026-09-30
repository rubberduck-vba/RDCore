using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.Runtime.Semantics.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.6 Parenthesized Expressions</strong> (runtime semantics): the operator a pair of
/// parentheses around an expression is.
/// </summary>
/// <remarks>
/// "A parenthesized expression evaluates to the simple data value of its enclosed expression", and "the
/// declared type of a parenthesized expression is that of the enclosed expression" — so this coerces its
/// operand to the operand's own declared type, and the effective type is never anything else. A
/// coercion that converts nothing is still a coercion, and that is the whole point of this operator:
/// what it yields is a <em>value</em>, not the variable the operand may have been.
/// <para>
/// That is what makes <c>Foo (x)</c> pass <c>x</c> by value to a <c>ByRef</c> parameter while
/// <c>Foo x</c> passes the variable itself. Nothing implements "forced ByVal" as a rule: the result of
/// this operator is a free-floating value bound to nothing, so there is no variable for a reference
/// parameter to alias, and argument passing already copies in that case.
/// </para>
/// </remarks>
public sealed record class UnaryLetCoerceOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : UnaryOperatorRuntimeSemantics<ConversionOperationSemanticContext, ConversionSemanticFlags>(LetCoercionProvider, FormatterService)
{
    protected override ISemanticContextContributor<ConversionOperationSemanticContext, ConversionSemanticFlags> AnalyzeOperands(
        ISemanticContextContributor<ConversionOperationSemanticContext, ConversionSemanticFlags> builder,
        DetermineOperatorEffectiveTypeResult effectiveType,
        params VBTypedValue[] operands)
        // written in the source as parentheses rather than as a conversion function, but a coercion the
        // program asked for all the same - the same flag BinaryLetCoerceOperatorRuntimeSemantics records.
        => builder.AddFlags(ConversionSemanticFlags.Explicit);

    protected override OperatorAnalysisContext<ConversionSemanticFlags> CreateAnalysisContext(
        SyntaxNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        ConversionSemanticFlags semanticFlags)
        => new(node.Identity, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    /// <summary>
    /// MS-VBAL 5.6.6: "the declared type of a parenthesized expression is that of the enclosed
    /// expression" — always applicable, so the arithmetic table the base falls back to (which would make
    /// a <c>String</c> operand's effective type <c>Double</c>) is never reached.
    /// </summary>
    protected override DetermineOperatorEffectiveTypeResult DetermineOperatorEffectiveType(
        ISymbolResolver resolver,
        ExpressionNode expression,
        OperatorEvaluationFrame frame)
        => DetermineOperatorEffectiveTypeResult.Success(frame[InputIndex.UnaryOperand].TypeInfo);

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        var coercionResult = LetCoercionProvider.EvaluateLetCoercionSemantics(resolver, expression,
            new(NodeId: expression.Identity,
                OperandIndex: InputIndex.UnaryOperand,
                SourceValue: frame[InputIndex.UnaryOperand],
                DestinationTypeDesc: new VBTypeDescValue(frame.EffectiveType)));

        return coercionResult.IsSuccess
            ? RuntimeSemanticsEvaluationResult.Success(coercionResult.Result!)
            : RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo
                ?? OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression,
                    Exceptions.LetCoercionRuntimeErrorExceptionTypeMismatch_Verbose), coercionResult.Result);
    }
}
