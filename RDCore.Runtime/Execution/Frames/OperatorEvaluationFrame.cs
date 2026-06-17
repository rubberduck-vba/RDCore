using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics;
using System.Collections.Immutable;

namespace RDCore.Runtime.Execution.Frames
{
    /// <summary>
    /// Represents a <see cref="OperatorRuntimeSemantics{TContext, TFlags}"/> evaluation step.
    /// </summary>
    /// <param name="NodeUri">The <c>SemanticId</c> of the associated expression node.</param>
    /// <param name="OperatorSymbol">The <see cref="StaticSymbol"/> representing the <strong>unallocated</strong> <em>language-level operator</em> symbol.</param>
    /// <param name="Operands">The resolved <see cref="VBTypedValue"/> values of the operand inputs of the operator.</param>
    /// <param name="EffectiveType">The <em>effective data type</em> of the operator expression, if determined.</param>
    /// <remarks>
    /// The <c>EffectiveType</c> is <see cref="VBUnknownType"/> if undetermined.
    /// </remarks>
    public readonly record struct OperatorEvaluationFrame(
        Uri NodeUri,
        StaticSymbol OperatorSymbol,
        ImmutableArray<VBTypedValue> Operands,
        VBType EffectiveType) : IStackFrame<InputIndex>
    {
        /// <summary>
        /// Gets the operand at the specified <c>index</c>.
        /// </summary>
        /// <param name="index">The <see cref="InputIndex"/> value describing the index of the operator to retrieve.</param>
        /// <returns>The <see cref="VBTypedValue"/> operand at the specified index.</returns>
        public VBTypedValue this[InputIndex index] => Operands[Convert.ToInt32(index)];

        Uri IStackFrame.NodeUri => NodeUri;
        StaticSymbol IStackFrame.StaticSymbol => OperatorSymbol;
        ImmutableArray<VBTypedValue> IStackFrame.Inputs => Operands;
    };
}
