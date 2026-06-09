using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Operators.Context;

namespace RDCore.SDK.Semantics.Runtime.Abstract;

/// <summary>
/// Represents any runtime semantics rules.
/// </summary>
/// <typeparam name="TContext">The type of <c>SemanticContext</c> for this semantic operation.</typeparam>
/// <typeparam name="TFlags">The type of <em>semantic flags</em> of the semantic context.</typeparam>
public interface IRuntimeSemantics<TContext, TFlags>
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{

    /// <summary>
    /// Evaluates the specified <c>BoundNode</c> in the specified execution context, using the specified inputs.
    /// </summary>
    /// <remarks>
    /// ⚠️ <strong>Does not throw</strong> any run-time errors; instead it packages the error metadata in the result.
    /// </remarks>
    /// <param name="runtime">The execution context and memory space to operate with.</param>
    /// <param name="context">The semantic context of this operation, built by <c>Analyze</c>.</param>
    /// <param name="node">The bound node to be evaluated.</param>
    /// <param name="inputs">The inputs of the bound node.</param>
    RuntimeSemanticsEvaluationResult Evaluate(
        IVBExecutionContext runtime, 
        SemanticContext<TFlags> context, 
        BoundNode<TContext, TFlags> node, 
        params VBTypedValue[] inputs);
    
    /// <summary>
    /// Analyzes the specified <c>BoundNode</c> in the specified execution context, using the specified inputs.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context..</param>
    /// <param name="builder">A <em>semantic flags builder</em> specifically for the operation defined by the <c>node</c> under scrutiny.</param>
    /// <param name="node">The bound node to analyze.</param>
    /// <param name="inputs">The inputs of the bound node.</param>
    /// <returns>
    /// Returns its <c>builder</c> parameter.
    /// </returns>
    ISemanticFlagsAccumulator<TFlags> Analyze(
        ISymbolResolver resolver, 
        ConversionOperationSemanticContext conversionContext, 
        ISemanticFlagsAccumulator<TFlags> builder, 
        BoundNode<TContext, TFlags> node, 
        params VBTypedValue[] inputs);
}
