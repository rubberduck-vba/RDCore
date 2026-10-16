using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Facts;
using System.Collections.Immutable;

namespace RDCore.Runtime.Execution.Frames
{
    /// <summary>
    /// Represents a <see cref="OperatorRuntimeSemantics{TContext, TFlags}"/> evaluation step.
    /// </summary>
    /// <param name="NodeId">The <c>Identity</c> of the associated expression node.</param>
    /// <param name="Operands">The resolved <see cref="VBTypedValue"/> values of the operand inputs of the operator.</param>
    /// <param name="EffectiveType">The <em>effective data type</em> of the operator expression, if determined.</param>
    /// <param name="Comparison">How the operator compares <c>String</c> values where it is evaluated (<strong>MS-VBAL 5.6.9.5</strong>).</param>
    /// <param name="Site">The construct that asks for the operation, which is the site of the conversions of its operands.</param>
    /// <remarks>
    /// The <c>EffectiveType</c> is <see cref="VBUnknownType"/> if undetermined.
    /// </remarks>
    public readonly record struct OperatorEvaluationFrame(
        SyntaxNodeId NodeId,
        ImmutableArray<VBTypedValue> Operands,
        VBType EffectiveType,
        StringComparisonRules Comparison = default,
        ConversionSite Site = ConversionSite.OperatorOperand) : IStackFrame<InputIndex>
    {
        /// <summary>
        /// Gets the operand at the specified <c>index</c>.
        /// </summary>
        /// <param name="index">The <see cref="InputIndex"/> value describing the index of the operator to retrieve.</param>
        /// <returns>The <see cref="VBTypedValue"/> operand at the specified index.</returns>
        public VBTypedValue this[InputIndex index] => Operands[(int)index];

        /// <summary>
        /// Whether an operand has a declared type of <c>Variant</c>: what an arithmetic operation that overflows its effective type does depends on it
        /// (<strong>MS-VBAL 5.6.9.3</strong>).
        /// </summary>
        /// <remarks>
        /// The operands are let-coerced to the effective type before the operation is evaluated, which leaves no Variant among them: this is said of the
        /// operands as the operation was given them, where a Variant operand is the <see cref="VBVariantValue"/> its declaration reads as.
        /// </remarks>
        public bool HasVariantOperand { get; init; }

        SyntaxNodeId IStackFrame.NodeId => NodeId;
        ImmutableArray<VBTypedValue> IStackFrame.Inputs => Operands;
    };
}
