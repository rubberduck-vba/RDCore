using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Semantics.Context.Abstract;

/// <summary>
/// Represents the semantic context of an operator expression.
/// </summary>
/// <typeparam name="TFlags">The specific type of semantic flags in this context.</typeparam>
public abstract record class OperatorSemanticContext<TFlags> : SemanticContext<TFlags> where TFlags: struct, Enum
{
    /// <summary>
    /// Gets the <em>effective type</em> of the operation, as determined by the data type of the operand(s).
    /// </summary>
    /// <remarks>
    /// 👉 Represents the outcome of the first step of the operator expression evaluation process. A <em>type mismatch</em> error is thrown at run-time is no <em>effective type</em> can be determined.
    /// </remarks>
    public VBType? EffectiveType { get; init; }
    /// <summary>
    /// Gets the operands after they have undergone let-coercion and validation as applicable.
    /// </summary>
    /// <remarks>
    /// Failed validation (or let-coercion) throws a <em>type mismatch</em> error at run-time.
    /// </remarks>
    public VBTypedValue[] ValidOperands { get; init; } = [];
    /// <summary>
    /// Gets the evaluated operation result if it can be evaluated in a semantic analysis context.
    /// </summary>
    /// <remarks>
    /// 
    /// </remarks>
    public VBTypedValue? OperationResult { get; init; }
}