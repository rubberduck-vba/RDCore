using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract.Operators;
using RDCore.SDK.Semantics.Runtime.LetCoercion;
using RDCore.SDK.Semantics.Runtime.Operators.Context;
using RDCore.SDK.Services.VerboseMessages;
using System.Diagnostics;

namespace RDCore.SDK.Semantics.Runtime.Abstract;

/// <summary>
/// <strong>MS-VBAL 5.6.9.2 Simple Data Operators</strong><br/>
/// 👉 <em>Simple data operators</em> are operators that first evaluate their operands as <em>simple data values</em>.
/// </summary>
/// <typeparam name="TContext">The specific type of <see cref="SemanticContext&lt;TFlags&gt;"/> associated with the operator.</typeparam>
/// <typeparam name="TFlags">The type of semantic flags being accumulated.</typeparam>
/// <param name="LetCoercionSemanticsProvider">A service that provides the type-appropriate <em>let-coercion runtime semantics</em> on demand.</param>
/// <param name="FormatterService">A service that builds the configurable <c>Verbose</c> message string of a <see cref="VBErrorInfo"/>.</param>
/// <remarks>
/// Encapsulates the base runtime semantics of <em>operator expressions</em>.
/// </remarks>
public abstract record class OperatorRuntimeSemantics<TContext, TFlags>(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider, 
    IVerboseMessageBuilder FormatterService)
    : RuntimeSemantics<TContext, TFlags>
where TContext : SemanticContext<TFlags>, new()
where TFlags : struct, Enum
{
    /// <summary>
    /// A service that provides the type-appropriate <em>let-coercion runtime semantics</em> on demand.
    /// </summary>
    protected ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider { get; } = LetCoercionSemanticsProvider;
    /// <summary>
    /// A service that builds the configurable <c>Verbose</c> message string of a <see cref="VBErrorInfo"/>.
    /// </summary>
    protected IVerboseMessageBuilder FormatterService { get; } = FormatterService;

    public sealed override ISemanticContextContributor<TContext, TFlags> Analyze(
        ISymbolResolver resolver, 
        ConversionOperationSemanticContext conversionContext, 
        ISemanticFlagsAccumulator<TFlags> builder, 
        BoundNode<TContext, TFlags> node, 
        params VBTypedValue[] inputs)
        => Analyze(resolver, conversionContext, builder, (VBOperatorExpression<TContext, TFlags>)node, inputs);

    /// <summary>
    /// Analyzes the specified <c>VBOperatorExpression</c> node in the specified execution context, using the specified operands.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context..</param>
    /// <param name="builder">A <em>semantic flags builder</em> specifically for the operation defined by the <see cref="VBOperatorExpression{TContext,TFlags}"/> node under scrutiny.</param>
    /// <param name="expression">The <em>operator expression</em> node to be evaluated.</param>
    /// <param name="operands">The operands of the <em>operator expression</em>.</param>
    protected ISemanticContextContributor<TContext, TFlags> Analyze(
        ISymbolResolver resolver, 
        ISemanticContextContributor<TContext, TFlags> builder, 
        VBOperatorExpression<TContext, TFlags> expression,
        params VBTypedValue[] operands)
    {
        var initialContext = ((ISemanticContextBuilder<TContext, TFlags>)builder).Build();
        var conversionContextBuilder = new LetCoercionSemanticContextFlagsBuilder();
        var operandsInfo = operands.Select((operand, index) => (operand.TypeInfo, Index:(InputIndex)index)).ToArray();

        // 1. determine the effective type:
        var frame = new OperatorEvaluationFrame
        {
            NodeUri = expression.SemanticId,
            OperatorSymbol = expression.Symbol,
            Operands = [.. operands],
            EffectiveType = VBUnknownType.TypeInfo,
        };

        var effectiveTypeResult = DetermineOperatorEffectiveType(resolver, initialContext, expression, frame);
        frame = frame with { EffectiveType = effectiveTypeResult.Result ?? frame.EffectiveType };

        // 2. validate the operands (let-coerce non-null operands):
        var coercionResult = operandsInfo
            .Select(info => AnalyzeValidateOperand(resolver, conversionContextBuilder, expression, frame, info.Index))
            // merging the results aggregates their respective sub operations into a single unified coercion stack:
            .Aggregate((context, operation) => context.Merge(operation));

        // 3. evaluate the result:
        var evaluationResult = EvaluateExpressionResult((IVBExecutionContext)resolver, initialContext, expression, frame);

        // 4. ...profit:
        var analysisContext = CreateAnalysisContext(expression, effectiveTypeResult, coercionResult, evaluationResult, initialContext.Flags);
        return Analyze(resolver, conversionContextBuilder.Build(), builder, expression, analysisContext, [.. frame.Operands]);
    }

    /// <summary>
    /// Creates and returns the specific <see cref="OperatorAnalysisContext{TFlags}"/> instance for this operator.
    /// </summary>
    /// <param name="node">The <em>operator expression bound node</em>.</param>
    /// <param name="determineOperatorEffectiveTypeResult">The result of the <em>determine effective type</em> (first) evaluation step.</param>
    /// <param name="coercionResult">The result of the <em>validate operand data types</em> (second) evaluation step.</param>
    /// <param name="evaluationResult">The result of the <em>evaluate result</em> (third/last) evaluation step.</param>
    /// <param name="semanticFlags">The <em>semantic flags</em> associated with this operation evaluation.</param>
    /// <returns></returns>
    protected abstract OperatorAnalysisContext<TFlags> CreateAnalysisContext(BoundNode<TContext, TFlags> node, 
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        TFlags semanticFlags);

    /// <summary>
    /// Analyzes the specified <c>VBOperatorExpression</c> node in the specified execution context, using the specified operands.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context..</param>
    /// <param name="coercionContext">The <em>let-coercion</em> semantic context of this operation.</param>
    /// <param name="builder">A <em>semantic flags builder</em> specifically for the operation defined by the <see cref="VBOperatorExpression{TContext,TFlags}"/> node under scrutiny.</param>
    /// <param name="analysisContext">The results of each step of the analysis/evaluation process.</param>
    /// <param name="expression">The <em>operator expression</em> node to be evaluated.</param>
    /// <param name="operands">The operands of the <em>operator expression</em>.</param>
    /// <remarks>
    /// <list type="bullet">
    /// <item>If present, the <c>ErrorInfo</c> of the <c>effectiveTypeResult</c> has already been added as an error diagnostic to the context.</item>
    /// <item>This method is templated and invoked by the <em>semantic analysis pipeline</em> to incrementally build the <em>semantic flags</em> into the context.</item>
    /// </list>
    /// 🧩 <strong>There is no mechanism to <em>remove</em> a flag from the <em>builder</em></strong>; semantic flags should only be added in <c>sealed</c> implementations under most circumstances.
    /// </remarks>
    /// <returns>The <c>builder</c> parameter, or a reference to it returned by one of its methods.</returns>
    protected abstract ISemanticContextContributor<TContext, TFlags> Analyze(
        ISymbolResolver resolver, 
        ConversionOperationSemanticContext coercionContext, 
        ISemanticContextContributor<TContext, TFlags> builder,
        VBOperatorExpression<TContext, TFlags> expression, 
        OperatorAnalysisContext<TFlags> analysisContext, 
        params VBTypedValue[] operands);

    /// <summary>
    /// Determines the <em>effective type</em> of an operation based on the data type of its operands.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context..</param>
    /// <param name="node">The <em>expression node</em> to analyze.</param>
    /// <returns><strong>Does not throw exceptions.</strong> Returns <c>DetermineOperatorEffectiveTypeResult.NotApplicable</c> if no type is statically valid.</returns>
    public abstract DetermineOperatorEffectiveTypeResult DetermineOperatorEffectiveType(
        ISymbolResolver resolver, 
        SemanticContext<TFlags> context, VBOperatorExpression
        <TContext, TFlags> expression,
        OperatorEvaluationFrame frame);

    protected sealed override RuntimeSemanticsEvaluationResult EvaluateSemanticNodeResult(
        ISymbolResolver resolver, 
        SemanticContext<TFlags> context, 
        BoundExpressionNode<TContext, TFlags> expression, 
        VBType effectiveType, 
        params VBTypedValue[] inputs)
    {
        var operatorNode = (VBOperatorExpression<TContext, TFlags>)expression;
        var frame = new OperatorEvaluationFrame(operatorNode.SemanticId, operatorNode.Symbol, [.. inputs], VBUnknownType.TypeInfo);
        return Evaluate(resolver, context, operatorNode, frame);
    }

    /// <summary>
    /// Evaluates the specified <c>operator expression</c> in the specified execution context, using the specified operands.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context..</param>
    /// <param name="context">The semantic context of this operation, built by <c>Analyze</c>.</param>
    /// <param name="expression">The operator expression being evaluated.</param>
    /// <param name="operands">The operand(s) of the operator expression.</param>
    protected RuntimeSemanticsEvaluationResult Evaluate(
        ISymbolResolver resolver, 
        SemanticContext<TFlags> context, 
        VBOperatorExpression<TContext, TFlags> expression, 
        OperatorEvaluationFrame frame)
    {
        // 1. Determine the EFFECTIVE TYPE of the operation base on the type of its operands.
        var effectiveTypeResult = DetermineOperatorEffectiveType(resolver, context, expression, frame);
        // if no effective type can be determined, we must throw a type mismatch error:
        if (effectiveTypeResult.ErrorInfo is VBRuntimeErrorInfo error)
        {
            return RuntimeSemanticsEvaluationResult.Error(error);
        }
        else if (!effectiveTypeResult.IsApplicable)
        {
            // IMPLEMENTATION NOTE: this block is defensive / just to be thorough - this type mismatch is normally already handled.
            Debug.Fail("⚠️ Broken assumption: DetermineEffectiveType was expected to yield a TypeMismatch error in this situation.");
            var operandTypeNames = string.Join(',', frame.Operands.Select(operand => operand.TypeInfo.Name));
            
            return RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression,
                Exceptions.VBRuntimeTypeMismatch_OperationEffectiveType_Verbose.Replace("{$OPERANDS}", operandTypeNames)));
        }

        if (effectiveTypeResult.Result is VBType effectiveType) // this should be a given
        {
            // 2. Validate the operands (let-coercion and overflow checks).
            var validOperands = Enumerable.Range(0, frame.Operands.Length)
                .Select(index => ValidateOperand(resolver, expression, frame, (InputIndex)index))
                .Where(validation => validation.Result is not null)
                .Select(validation => validation.Result)
                .Cast<VBTypedValue>();

            // 3. Evaluate the result.
            var evaluateResult = EvaluateExpressionResult((IVBExecutionContext)resolver, context, expression, frame with { Operands = [.. validOperands] });
            if (evaluateResult.IsInternalError)
            {
                return RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.InternalError, expression, 
                    Exceptions.VBRuntimeInternalError_EvaluateOperatorRuntimeSemanticsNullApplicableResult_Verbose));
            }
            return evaluateResult;
        }
        
        // if we make it this far, something went horribly wrong.
        Debug.Fail("⚠️ Broken assumption: DetermineOperatorEffectiveTypeResult.Result was expected to yield a valid VBType value.");
        return RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.InternalError, expression,
            Exceptions.VBRuntimeInternalError_EvaluateOperatorRuntimeSemanticsNullApplicableResult_Verbose
                .Replace("{$EXPRESSION}", expression.GetType().Name)));
    }

    /// <summary>
    /// Evaluates a resulting <c>VBTypedValue</c> for a given <c>BoundExpression</c>.
    /// </summary>
    /// <param name="runtime">The current execution context..</param>
    /// <param name="context">The semantic context of this operation, built by <c>Analyze</c>.</param>
    /// <param name="expression">Any <c>BoundExpression</c> to be evaluated.</param>
    /// <param name="frame">The <see cref="OperatorEvaluationFrame"/> holding the semantic evaluation inputs.</param>
    protected abstract RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        IVBExecutionContext runtime, 
        SemanticContext<TFlags> context, VBOperatorExpression<TContext, TFlags> expression, 
        OperatorEvaluationFrame frame);

    /// <summary>
    /// Performs the <em>operand validation</em> step of operation evaluation runtime semantics.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context.</param>
    /// <param name="expression">The operator expression being evaluated.</param>
    /// <param name="frame">The operation evaluation frame.</param>
    protected LetCoercionResult ValidateOperand(
        ISymbolResolver resolver,
        VBOperatorExpression<TContext, TFlags> expression,
        OperatorEvaluationFrame frame,
        InputIndex index)
    {
        // IMPLEMENTATION NOTE: specifications explicitly duplicate the behavior here:
        //   MS-VBAL 5.6.9.3 Arithmetic Operators
        //   MS-VBAL 5.6.9.5 Relational Operators
        //   MS-VBAL 5.6.9.8 Logical Operators
        var operand = frame[index];
        return operand is VBNullValue
            ? LetCoercionResult.Success(operand, []) // NOTE: no coercion flags applicable here
            : LetCoerceNonNullOperand(resolver, expression, frame, index);
    }

    protected LetCoercionAnalysisContext AnalyzeValidateOperand(
        ISymbolResolver resolver,
        ILetCoercionSemanticContextBuilder builder,
        VBOperatorExpression<TContext, TFlags> expression,
        OperatorEvaluationFrame frame,
        InputIndex operandIndex)
    {
        var operand = frame[operandIndex];
        return operand is VBNullValue
            ? new LetCoercionAnalysisContext(frame.NodeUri, LetCoercionResult.Success(operand, []))
            : LetCoercionSemanticsProvider.Analyze(resolver, builder, expression,
                new()
                {
                    NodeUri = expression.SemanticId,
                    InputIndex = operandIndex,
                    SourceValue = operand,
                    DestinationTypeDesc = VBTypedValueFactory.DescribeType(frame.EffectiveType, expression.ResultSymbol),
                });
    }

    /// <summary>
    /// Let-coerces the non-null operands of an <em>operator expression</em>.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context..</param>
    /// <param name="expression">The operator expression being evaluated.</param>
    /// <param name="frame">The evaluation frame of the operator expression.</param>
    /// <param name="operandIndex">The semantic position of the <c>sourceValue</c> in the <c>expression</c>.</param>
    /// <remarks>
    /// 👉 The <c>Frame</c> of the returned coercion result is created even when the frame is no-op.
    /// </remarks>
    protected LetCoercionResult LetCoerceNonNullOperand(
        ISymbolResolver resolver,
        VBOperatorExpression<TContext, TFlags> expression,
        OperatorEvaluationFrame frame,
        InputIndex operandIndex)
    {
        var operand = frame[operandIndex];
        Debug.Assert(operand is not VBNullValue);

        return frame.EffectiveType.Equals(operand.TypeInfo)
            // if the type of the operand is the effective type, the result is the unchanged operand (no coercion occurs).
            ? LetCoercionResult.Success(operand)
            : LetCoercionSemanticsProvider.EvaluateLetCoercionSemantics(resolver, expression, new() {
                NodeUri = expression.SemanticId,
                InputIndex = operandIndex,
                SourceValue = operand,
                DestinationTypeDesc = VBTypedValueFactory.DescribeType(frame.EffectiveType, expression.ResultSymbol),
            });
    }
}
