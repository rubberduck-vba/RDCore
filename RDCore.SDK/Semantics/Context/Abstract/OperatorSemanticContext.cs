using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using System.Collections.Immutable;

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

    /// <summary>
    /// Gets the conversion semantic context of each operand of the operation, in operand order (<see cref="InputIndex"/>): what
    /// the operation does to that operand to make it a value it can operate on, and the facts about that operand.
    /// </summary>
    /// <remarks>
    /// 👉 Empty for a context that was not built by an analysis. Read a specific operand's with <see cref="ConversionContextOf"/>.
    /// </remarks>
    public ImmutableArray<ConversionOperationSemanticContext> OperandConversionContexts { get; init; } = [];

    /// <summary>
    /// Gets the conversion semantic context of the specified operand: its let-coercion flags (<c>LetCoerced</c>, <c>Implicit</c>,
    /// <c>Widening</c>, ...), and what is known about the operand itself (<c>NullOperand</c>, <c>ObjectOperand</c>, ...).
    /// </summary>
    /// <param name="operand">The operand to get the conversion semantic context of.</param>
    /// <returns>An empty context if nothing is known about the operand, for instance because the context was not built by an analysis.</returns>
    public ConversionOperationSemanticContext ConversionContextOf(InputIndex operand)
        => (int)operand < OperandConversionContexts.Length ? OperandConversionContexts[(int)operand] : new();
}