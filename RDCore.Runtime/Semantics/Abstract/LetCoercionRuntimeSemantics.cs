using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Semantics.Abstract;

/// <summary>
/// The base class for all <em>let-coercion strategies</em>.
/// </summary>
/// <typeparam name="TStrategy">The <see cref="VBType"/> for which this <em>let-coercion runtime semantics</em> implementation is applicable.</typeparam>
/// <remarks>
/// 👉 Let-coercion runtime mechanics specifically <strong>do not inherit</strong> <c>RuntimeSemantics</c>.
/// </remarks>
/// <see cref="LetCoercionRuntimeSemanticsProvider"/>
public abstract record class LetCoercionRuntimeSemantics<TStrategy> : ILetCoercionRuntimeSemantics
    where TStrategy : VBType
{
    private readonly IVerboseMessageBuilder _formatterService;
    protected LetCoercionRuntimeSemantics(IVerboseMessageBuilder formatterService)
    {
        _formatterService = formatterService;
    }

    public Type LetCoercionSpecification { get; } = typeof(TStrategy);

    /// <summary>
    /// Evaluates let-coercion runtime semantics.
    /// </summary>
    /// <typeparam name="TContext">The type of <em>semantic context</em> of the <c>expression</c> the let-coercion is occurring inside of.</typeparam>
    /// <typeparam name="TFlags">The type of semantic flags associated with the semantic context of the <c>expression</c>.</typeparam>
    /// <param name="resolver">A symbol lookup service.</param>
    /// <param name="expression">The <c>BoundExpression</c> that is being evaluated.</param>
    /// <param name="frame">The current stack frame of the coercion operation.</param>
    /// <returns></returns>
    public abstract LetCoercionResult EvaluateLetCoercion<TContext, TFlags>(
        ISymbolResolver resolver, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum;

    /// <summary>
    /// Override this method to add specialized flags and/or diagnostics to the semantic context of a <em>let-coercion</em> operation.
    /// </summary>
    /// <typeparam name="TContext">The type of <em>semantic context</em> of the <c>expression</c> the let-coercion is occurring inside of.</typeparam>
    /// <typeparam name="TFlags">The type of semantic flags associated with the semantic context of the <c>expression</c>.</typeparam>
    /// <param name="builder">Builds the semantic context of the conversion operation.</param>
    /// <param name="resolver">A symbol lookup service.</param>
    /// <param name="expression">The <c>BoundExpression</c> that is being evaluated.</param>
    /// <param name="frame">The current stack frame of the coercion operation.</param>
    /// <param name="result">The result of the let-coercion operation for the current stack frame.</param>
    /// <returns>The <see cref="LetCoercionAnalysisContext"/> for the context of this <em>let-coercion</em> operation.</returns>
    public LetCoercionAnalysisContext Analyze<TContext, TFlags>(
        ILetCoercionSemanticContextBuilder builder, 
        ISymbolResolver resolver, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame,
        LetCoercionResult result)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
    {
        var coercionSemanticContext = AnalyzeLetCoercionOperation(builder, resolver, expression, frame);
        var flags = coercionSemanticContext.Flags;

        if (flags.HasFlag(ConversionSemanticFlags.Failed))
        {
            builder.AddOnError(result.ErrorInfo);
            return new(expression.SemanticId, result, flags);
        }
        return new(expression.SemanticId, LetCoercionResult.NotApplicable(frame), flags);
    }

    /// <summary>
    /// Uses a <em>semantic context flags builder</em> to incrementally build the semantic flags in the context of a specific let-coercion evaluation frame.
    /// </summary>
    /// <typeparam name="TContext">The type of <em>semantic context</em> of the <c>expression</c> the let-coercion is occurring inside of.</typeparam>
    /// <typeparam name="TFlags">The type of semantic flags associated with the semantic context of the <c>expression</c>.</typeparam>
    /// <param name="builder">Builds the semantic context of the conversion operation.</param>
    /// <param name="resolver">A symbol lookup service.</param>
    /// <param name="expression">The <c>BoundExpression</c> that is being evaluated.</param>
    /// <param name="frame">The current stack frame of the coercion operation.</param>
    /// <returns></returns>
    protected abstract ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation<TContext, TFlags>(
        ILetCoercionSemanticContextBuilder builder, 
        ISymbolResolver resolver, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum;

    /// <summary>
    /// Validates that the <c>sourceValue</c> is within the range of <em>destination declared type</em>.
    /// </summary>
    /// <param name="error">Any run-time error that needs to be thrown following this validation, if it failed.</param>
    /// <returns><c>true</c> if the specified <c>sourceValue</c> is within the range of the <c>destinationDeclaredType</c>; <c>false</c> otherwise.</returns>
    protected bool ValidateDestinationTypeRange(BoundExpression expression, LetCoercionStackFrame frame, [MaybeNullWhen(true)][NotNullWhen(false)] out VBRuntimeErrorInfo? error)
    {
        error = VBNumericType.IsWithinRange(frame.SourceValue.ManagedValue.InteropValue!.Value.Double, (VBNumericType)frame.DestinationTypeDesc.Target) 
            ? null : OnLetCoercionOverflow(expression, frame);
        // the value is within range if we don't have an impending overflow error:
        return error is null;
    }

    /// <summary>
    /// A helper method to get a <c>VBRuntimeErrorInfo</c> error metadata from derived types as needed.
    /// </summary>
    protected VBRuntimeErrorInfo OnLetCoercionTypeMismatch(BoundExpression expression, LetCoercionStackFrame frame) =>
        VBRuntimeErrorInfo.For(VBRuntimeErrorId.TypeMismatch, expression.Location,
            _formatterService.Format(Exceptions.LetCoercionRuntimeErrorExceptionTypeMismatch_Verbose, expression, [frame]));

    /// <summary>
    /// A helper method to get a <c>VBRuntimeErrorInfo</c> error metadata from derived types as needed.
    /// </summary>
    protected VBRuntimeErrorInfo OnLetCoercionObjectRequired(BoundExpression expression, LetCoercionStackFrame frame) =>
        VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectRequired, expression.Location,
            _formatterService.Format(Exceptions.LetCoercionRuntimeErrorExceptionObjectRequired, expression, [frame]));

    /// <summary>
    /// A helper method to get a <c>VBRuntimeErrorInfo</c> error metadata from derived types as needed.
    /// </summary>
    protected VBRuntimeErrorInfo OnLetCoercionOverflow(BoundExpression expression, LetCoercionStackFrame frame) =>
        VBRuntimeErrorInfo.For(VBRuntimeErrorId.Overflow, expression.Location,
            _formatterService.Format(Exceptions.LetCoercionRuntimeErrorExceptionOverflow_Verbose, expression, [frame]));

    /// <summary>
    /// A helper method to get a <c>VBRuntimeErrorInfo</c> error metadata from derived types as needed.
    /// </summary>
    protected VBRuntimeErrorInfo OnLetCoercionInvalidUseOfNull(BoundExpression expression, LetCoercionStackFrame frame) =>
        VBRuntimeErrorInfo.For(VBRuntimeErrorId.InvalidUseOfNull, expression.Location,
            _formatterService.Format(Exceptions.LetCoercionRuntimeErrorExceptionInvalidUseOfNull_Verbose, expression, [frame]));
}
