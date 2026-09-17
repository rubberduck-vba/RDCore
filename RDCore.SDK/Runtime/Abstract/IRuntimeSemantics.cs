using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;

namespace RDCore.SDK.Runtime.Abstract;

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
    /// Evaluates the specified <c>SyntaxNode</c> in the specified execution context, using the specified inputs.
    /// </summary>
    /// <remarks>
    /// ⚠️ <strong>Does not throw</strong> any run-time errors; instead it packages the error metadata in the result.
    /// </remarks>
    /// <param name="session">The current execution session — its <c>Symbols.Resolver</c> is the read
    /// face over the current execution context; <c>Objects</c>/<c>CallStack</c> are here for the
    /// runtime semantics that need to instantiate an object or push/pop an activation.</param>
    /// <param name="context">The semantic context of this operation, built by <c>Analyze</c>.</param>
    /// <param name="node">The bound node to be evaluated.</param>
    /// <param name="inputs">The inputs of the bound node.</param>
    RuntimeSemanticsEvaluationResult Evaluate(
        IRuntimeSession session,
        TContext context,
        SyntaxNode node,
        params VBTypedValue[] inputs);

    /// <summary>
    /// Analyzes the specified <c>SyntaxNode</c> in the specified execution context, using the specified inputs.
    /// </summary>
    /// <param name="session">The current execution session — its <c>Symbols.Resolver</c> is the read
    /// face over the current execution context.</param>
    /// <param name="builder">A <em>semantic flags builder</em> specifically for the operation defined by the <c>node</c> under scrutiny.</param>
    /// <param name="node">The bound node to analyze.</param>
    /// <param name="inputs">The inputs of the bound node.</param>
    /// <returns>
    /// Returns its <c>builder</c> parameter.
    /// </returns>
    ISemanticFlagsAccumulator<TFlags> Analyze(
        IRuntimeSession session,
        ConversionOperationSemanticContext conversionContext,
        ISemanticFlagsAccumulator<TFlags> builder,
        SyntaxNode node,
        params VBTypedValue[] inputs);
}
