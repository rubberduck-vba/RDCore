using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Symbols;
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
    /// <param name="NodeId">The <c>Identity</c> of the associated expression node.</param>
    /// <param name="Operands">The resolved <see cref="VBTypedValue"/> values of the operand inputs of the operator.</param>
    /// <param name="EffectiveType">The <em>effective data type</em> of the operator expression, if determined.</param>
    /// <param name="CompareMode">The mode the operator compares <c>String</c> values in where it is evaluated (<strong>MS-VBAL 5.2.1.1</strong>): never <c>Database</c>.</param>
    /// <remarks>
    /// The <c>EffectiveType</c> is <see cref="VBUnknownType"/> if undetermined.
    /// </remarks>
    public readonly record struct OperatorEvaluationFrame(
        SyntaxNodeId NodeId,
        ImmutableArray<VBTypedValue> Operands,
        VBType EffectiveType,
        OptionCompare CompareMode = OptionCompare.Binary) : IStackFrame<InputIndex>
    {
        /// <summary>
        /// Gets the operand at the specified <c>index</c>.
        /// </summary>
        /// <param name="index">The <see cref="InputIndex"/> value describing the index of the operator to retrieve.</param>
        /// <returns>The <see cref="VBTypedValue"/> operand at the specified index.</returns>
        public VBTypedValue this[InputIndex index] => Operands[Convert.ToInt32(index)];

        SyntaxNodeId IStackFrame.NodeId => NodeId;
        ImmutableArray<VBTypedValue> IStackFrame.Inputs => Operands;
    };
}
