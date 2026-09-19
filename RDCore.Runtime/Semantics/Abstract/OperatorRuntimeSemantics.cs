using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Values.Meta;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;
using System.Diagnostics;

namespace RDCore.Runtime.Semantics.Abstract;

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
        IRuntimeSession session,
        ConversionOperationSemanticContext conversionContext,
        ISemanticFlagsAccumulator<TFlags> builder,
        SyntaxNode node,
        params VBTypedValue[] inputs)
        // NOTE: pre-existing bug found while widening this signature - this used to call itself
        // recursively (its own argument shape didn't match the narrower 4-param Analyze(resolver,
        // builder, expression, operands) overload below it was clearly meant to delegate to; it only
        // compiled because both overloads' first parameter happened to be ISymbolResolver). Nothing
        // calls this Analyze path yet (RDCore.Diagnostics isn't wired to it), so it was latent.
        => Analyze(session.Symbols.Resolver, (ISemanticContextContributor<TContext, TFlags>)builder, (VBOperatorExpression)node, session.CurrentCompareMode(), inputs);

    /// <summary>
    /// Analyzes the specified <c>VBOperatorExpression</c> node in the specified execution context, using the specified operands.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context..</param>
    /// <param name="builder">A <em>semantic flags builder</em> specifically for the operation defined by the <see cref="VBOperatorExpression{TContext,TFlags}"/> node under scrutiny.</param>
    /// <param name="expression">The <em>operator expression</em> node to be evaluated.</param>
    /// <param name="compareMode">The mode the operation compares <c>String</c> values in where it is analyzed.</param>
    /// <param name="operands">The operands of the <em>operator expression</em>.</param>
    protected ISemanticContextContributor<TContext, TFlags> Analyze(
        ISymbolResolver resolver,
        ISemanticContextContributor<TContext, TFlags> builder,
        VBOperatorExpression expression,
        OptionCompare compareMode,
        params VBTypedValue[] operands)
    {
        // what has been contributed so far is the context the effective type is determined in; any builder that can build
        // a context will do - one that also carries diagnostics is not required.
        var initialContext = ((ISemanticContextFlagsBuilder<TContext, TFlags>)builder).Build();
        var conversionContextBuilder = new LetCoercionSemanticContextFlagsBuilder();
        var operandsInfo = operands.Select((operand, index) => (operand.TypeInfo, Index:(InputIndex)index)).ToArray();

        // 1. determine the effective type:
        var frame = new OperatorEvaluationFrame
        {
            NodeId = expression.Identity,
            //OperatorSymbol = expression.Symbol,
            Operands = [.. operands],
            EffectiveType = VBUnknownType.TypeInfo,
            CompareMode = compareMode,
        };

        var effectiveTypeResult = DetermineOperatorEffectiveType(resolver, initialContext, expression, frame);
        frame = frame with { EffectiveType = effectiveTypeResult.Result ?? frame.EffectiveType };

        // 2. validate the operands (let-coerce non-null operands); with no effective type there is nothing to coerce them to:
        var coercionResult = operandsInfo
            .Select(info => effectiveTypeResult.Result is null
                ? new LetCoercionAnalysisContext(frame.NodeId, LetCoercionResult.Success(frame[info.Index], []))
                : AnalyzeValidateOperand(resolver, conversionContextBuilder, expression, frame, info.Index))
            // merging the results aggregates their respective sub operations into a single unified coercion stack:
            .Aggregate((context, operation) => context.Merge(operation));

        // 3. evaluate the result - the way the operator itself would, which is on the operands as let-coerced to the
        //    effective type (evaluating the raw operands would hand a Double operation a Long), and which reports the
        //    error, if there is one, that stopped the operation: no effective type, a failed coercion, or the evaluation.
        var evaluationResult = EvaluateForAnalysis(resolver, initialContext, expression, frame);
        builder.AddOnError(evaluationResult.ErrorInfo);

        // 4. ...profit:
        var analysisContext = CreateAnalysisContext(expression, effectiveTypeResult, coercionResult, evaluationResult, initialContext.Flags)
            with { CompareMode = compareMode };
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
    protected abstract OperatorAnalysisContext<TFlags> CreateAnalysisContext(SyntaxNode node, 
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
        VBOperatorExpression expression, 
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
        TContext context, 
        VBOperatorExpression expression,
        OperatorEvaluationFrame frame);

    /// <summary>
    /// Evaluates the specified <c>expression</c> in the specified execution context, using the specified operands.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context.</param>
    /// <param name="context">The semantic context of this operation, built by <c>Analyze</c>.</param>
    /// <param name="node">The <em>expression node</em> to be evaluated.</param>
    /// <param name="inputs">The inputs of the expression.</param>
    public sealed override RuntimeSemanticsEvaluationResult Evaluate(
        IRuntimeSession session,
        TContext context,
        SyntaxNode node,
        params VBTypedValue[] inputs)
    {
        var expression = (VBOperatorExpression)node;
        var frame = new OperatorEvaluationFrame(expression.Identity, [.. inputs], VBUnknownType.TypeInfo, session.CurrentCompareMode());
        return Evaluate(session.Symbols.Resolver, context, expression, frame);
    }

    /// <summary>
    /// Evaluates the specified <c>operator expression</c> in the specified execution context, using the specified operands.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context..</param>
    /// <param name="context">The semantic context of this operation, built by <c>Analyze</c>.</param>
    /// <param name="expression">The operator expression being evaluated.</param>
    /// <param name="frame">The evaluation frame encapsulating the operation inputs.</param>
    protected RuntimeSemanticsEvaluationResult Evaluate(
        ISymbolResolver resolver, 
        TContext context, 
        VBOperatorExpression expression, 
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
            // the determined effective type must be in the frame before operand validation and
            // evaluation run, since both key their own dispatch off frame.EffectiveType.
            frame = frame with { EffectiveType = effectiveType };

            // 2. Validate the operands (let-coercion and overflow checks).
            var operandValidations = Enumerable.Range(0, frame.Operands.Length)
                .Select(index => ValidateOperand(resolver, expression, frame, (InputIndex)index))
                .ToArray();

            // a genuine coercion failure (e.g. Overflow) on any operand must short-circuit evaluation
            // here with its own error - silently dropping the operand would leave EvaluateExpressionResult
            // indexing into an array shorter than frame.Operands, and hide the real error entirely.
            foreach (var validation in operandValidations)
            {
                if (validation.Result is null)
                {
                    return RuntimeSemanticsEvaluationResult.Error(validation.ErrorInfo
                        ?? OnRuntimeError(VBRuntimeErrorId.InternalError, expression,
                            Exceptions.VBRuntimeInternalError_EvaluateOperatorRuntimeSemanticsNullApplicableResult_Verbose));
                }
            }

            var validOperands = operandValidations.Select(validation => validation.Result!);

            // 3. Evaluate the result.
            var evaluateResult = EvaluateExpressionResult(resolver, context, expression, frame with { Operands = [.. validOperands] });
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
    /// Evaluates a resulting <c>VBTypedValue</c> for a given <c>VBOperatorExpression</c>.
    /// </summary>
    /// <param name="runtime">The current execution context..</param>
    /// <param name="context">The semantic context of this operation, built by <c>Analyze</c>.</param>
    /// <param name="expression">Any <c>VBOperatorExpression</c> to be evaluated.</param>
    /// <param name="frame">The <see cref="OperatorEvaluationFrame"/> holding the semantic evaluation inputs.</param>
    protected abstract RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        ISymbolResolver resolver, 
        TContext context, VBOperatorExpression expression, 
        OperatorEvaluationFrame frame);

    /// <summary>
    /// Performs the <em>operand validation</em> step of operation evaluation runtime semantics.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context.</param>
    /// <param name="expression">The operator expression being evaluated.</param>
    /// <param name="frame">The operation evaluation frame.</param>
    protected virtual LetCoercionResult ValidateOperand(
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        OperatorEvaluationFrame frame,
        InputIndex index)
    {
        // IMPLEMENTATION NOTE: specifications explicitly duplicate the behavior here:
        //   MS-VBAL 5.6.9.3 Arithmetic Operators
        //   MS-VBAL 5.6.9.5 Relational Operators
        //   MS-VBAL 5.6.9.8 Logical Operators
        // A VBTypeDescValue operand (RD-VBAL 5.6.9.9's Let-coercion operator) is metadata describing a
        // coercion target, not a value to be converted — same exemption as VBNullValue.
        //
        // A Null EFFECTIVE type (e.g. a Numeric+Null operand pair in an arithmetic or relational
        // operator, MS-VBAL 5.6.9.3/5.6.9.5) is exempted the same way: every operator's own dispatch
        // unconditionally returns Null for a Null effective type without ever consulting the operand
        // values, so coercing the OTHER, non-null operand toward a "Null" destination is never
        // meaningful — and no strategy is registered to do it, so it used to surface as an internal
        // error before the dispatch's own Null case was ever reached.
        var operand = frame[index];
        return operand is VBNullValue or VBTypeDescValue || frame.EffectiveType is VBNullType
            ? LetCoercionResult.Success(operand, []) // NOTE: no coercion flags applicable here
            : LetCoerceNonNullOperand(resolver, expression, frame, index);
    }

    /// <summary>
    /// Evaluates the operation for the analysis of it, which is to say without its effect: analyzing an operator that has a
    /// side effect, an assignment, must not perform it.
    /// </summary>
    /// <remarks>
    /// 👉 Most operators evaluate to a value and nothing else, and are evaluated as they are; an operator with an effect overrides this.
    /// </remarks>
    protected virtual RuntimeSemanticsEvaluationResult EvaluateForAnalysis(
        ISymbolResolver resolver,
        TContext context,
        VBOperatorExpression expression,
        OperatorEvaluationFrame frame)
        => Evaluate(resolver, context, expression, frame);

    /// <summary>
    /// The analysis counterpart of <see cref="ValidateOperand"/>: describes the let-coercion of an operand of the operation, if it has one.
    /// </summary>
    /// <remarks>
    /// 👉 An operator whose <see cref="ValidateOperand"/> differs from the default overrides this one to match it: the analysis
    /// describes the coercion the operation performs, not another one.
    /// </remarks>
    protected virtual LetCoercionAnalysisContext AnalyzeValidateOperand(
        ISymbolResolver resolver,
        ILetCoercionSemanticContextBuilder builder,
        VBOperatorExpression expression,
        OperatorEvaluationFrame frame,
        InputIndex operandIndex)
    {
        var operand = frame[operandIndex];

        // a Null operand is not coerced (nothing to coerce it to), but it is a fact about the operand all the same.
        if (operand is VBNullValue)
        {
            builder.AddLetCoercionFlags(ConversionSemanticFlags.NullOperand | OperandPositionFlags.Of(expression, operandIndex), operandIndex);
        }

        // the same operands ValidateOperand exempts, and the same destination it coerces the others to.
        var destinationType = CoercionDestinationOf(frame);
        return operand is VBNullValue or VBTypeDescValue || frame.EffectiveType is VBNullType
            || destinationType.Equals(operand.TypeInfo) // no coercion occurs
            ? new LetCoercionAnalysisContext(frame.NodeId, LetCoercionResult.Success(operand, []))
            : AnalyzeOperandCoercion(resolver, builder, expression, operand, operandIndex, destinationType);
    }

    /// <summary>
    /// Describes the let-coercion of <paramref name="operand"/> to <paramref name="destinationType"/>, the way the operation asks for it.
    /// </summary>
    protected LetCoercionAnalysisContext AnalyzeOperandCoercion(
        ISymbolResolver resolver,
        ILetCoercionSemanticContextBuilder builder,
        VBOperatorExpression expression,
        VBTypedValue operand,
        InputIndex operandIndex,
        VBType destinationType)
    {
        var context = LetCoercionSemanticsProvider.Analyze(resolver, builder, expression,
            new()
            {
                NodeId = expression.Identity,
                OperandIndex = operandIndex,
                SourceValue = operand,
                DestinationTypeDesc = new VBTypeDescValue(destinationType),
            });

        builder.AddLetCoercionFlags(OperandConversionKind, operandIndex);
        return context;
    }

    /// <summary>
    /// Whether the operands of this operation are coerced <see cref="ConversionSemanticFlags.Implicit"/>ly, the way MS-VBAL
    /// operators do, or <see cref="ConversionSemanticFlags.Explicit"/>ly, the way RD-VBAL's let-coercion operator does.
    /// </summary>
    protected virtual ConversionSemanticFlags OperandConversionKind => ConversionSemanticFlags.Implicit;

    // a Date effective type is computed in Double (MS-VBAL 5.6.9.3 et al.): the operands are let-coerced to Double even
    // when their own declared type already is Date.
    private static VBType CoercionDestinationOf(OperatorEvaluationFrame frame)
        => frame.EffectiveType is VBDateType ? VBDoubleType.TypeInfo : frame.EffectiveType;

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
        VBOperatorExpression expression,
        OperatorEvaluationFrame frame,
        InputIndex operandIndex)
    {
        var operand = frame[operandIndex];
        Debug.Assert(operand is not VBNullValue);

        var destinationType = CoercionDestinationOf(frame);

        return destinationType.Equals(operand.TypeInfo)
            // if the type of the operand is the destination type, the result is the unchanged operand (no coercion occurs).
            ? LetCoercionResult.Success(operand)
            : LetCoercionSemanticsProvider.EvaluateLetCoercionSemantics(resolver, expression, new() {
                NodeId = expression.Identity,
                OperandIndex = operandIndex,
                SourceValue = operand,
                DestinationTypeDesc = new VBTypeDescValue(destinationType),
            });
    }
}
