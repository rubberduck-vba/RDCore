using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Services.Formatters;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Runtime.Abstract;

/// <summary>
/// Represents a <c>LetCoercionRuntimeSemantics</c> <em>evaluation frame</em>.
/// </summary>
/// <remarks>
/// 👉 This mechanism allows detecting <em>recursive let-coercion</em> and avoiding uncontrolled re-entry.
/// </remarks>
/// <param name="NodeUri">The <c>Uri</c> of the <em>bound node</em> being evaluated.</param>
/// <param name="InputIndex">Encodes the semantic index of the operand being evaluated.</param>
/// <param name="SourceValue">The <em>source value</em> being let-coerced in this frame.</param>
/// <param name="DestinationTypeDesc">Describes the <em>destination type</em> of the let-coercion operation. The described data type must be unwrapped from the descriptor.</param>
public readonly record struct LetCoercionStackFrame(
    Uri NodeUri,
    StaticSymbol StaticSymbol,
    InputIndex InputIndex,
    VBTypedValue SourceValue,
    VBTypeDescValue DestinationTypeDesc) : IStackFrame<LetCoercionInputs>
{
    Uri IStackFrame.NodeUri => NodeUri;
    StaticSymbol IStackFrame.StaticSymbol => StaticSymbol;
    ImmutableArray<VBTypedValue> IStackFrame.Inputs => [SourceValue, DestinationTypeDesc];

    VBTypedValue IStackFrame<LetCoercionInputs>.this[LetCoercionInputs value] => 
        value == LetCoercionInputs.SourceValue ? SourceValue : DestinationTypeDesc;
}
