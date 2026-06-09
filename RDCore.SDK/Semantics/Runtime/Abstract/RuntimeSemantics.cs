using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Operators.Context;

namespace RDCore.SDK.Semantics.Runtime.Abstract;


/// <summary>
/// The class at the base of the runtime semantics type hierarchy that implements all the runtime semantic rules defined in MS-VBAL.
/// </summary>
/// <typeparam name="TContext">The </typeparam>
/// <typeparam name="TFlags"></typeparam>
public abstract record class RuntimeSemantics<TContext, TFlags>() : IRuntimeSemantics<TContext, TFlags>
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{
    public abstract ISemanticFlagsAccumulator<TFlags> Analyze(
        ISymbolResolver resolver, 
        ConversionOperationSemanticContext conversionContext, 
        ISemanticFlagsAccumulator<TFlags> builder, 
        BoundNode node, 
        params VBTypedValue[] inputs);
    
    public abstract RuntimeSemanticsEvaluationResult Evaluate(
        IVBExecutionContext runtime, 
        SemanticContext<TFlags> context, 
        BoundNode node, 
        params VBTypedValue[] inputs);

    /// <summary>
    /// Evaluates the specified <c>expression</c> in the specified execution context, using the specified inputs and returning a <em>semantic result</em> for analysis.
    /// </summary>
    /// <remarks>
    /// ⚠️ <strong>Must not throw, nor implicate any side-effecting run-time calls.</strong> Base implementation invokes the base <c>abstract Evaluate</c> templated method.
    /// </remarks>
    /// <param name="resolver">A read-only interface over the current execution context..</param>
    /// <param name="context">The semantic context of this operation, built by <c>Analyze</c>.</param>
    /// <param name="expression">The <c>VBOperatorExpression</c> to be evaluated.</param>
    /// <param name="effectiveType">The <em>effective type</em> of the operation.</param>
    /// <param name="inputs">The inputs of the expression.</param>
    protected virtual RuntimeSemanticsEvaluationResult EvaluateSemanticResult(ISymbolResolver resolver, SemanticContext<TFlags> context, BoundExpression expression, VBType effectiveType, params VBTypedValue[] inputs) 
        => Evaluate((IVBExecutionContext)resolver, context, expression, inputs);

    /// <summary>
    /// A helper method to get a <c>VBRuntimeErrorInfo</c> error metadata from derived types as needed.
    /// </summary>
    protected static VBRuntimeErrorInfo OnRuntimeError(VBRuntimeErrorId errorId, BoundExpression expression, string verbose)
        => new(errorId, expression.Location, VBRuntimeErrorException.GetErrorString(errorId), verbose);
}
