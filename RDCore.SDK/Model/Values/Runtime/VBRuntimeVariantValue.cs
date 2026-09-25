using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Runtime;

/// <summary>
/// Wraps a <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/>'s own wrapped
/// <see cref="VBTypedValue"/> for storage inside an <see cref="IRuntimeValue"/>, so a Variant
/// round-trips through <c>ISessionStorage</c> like any other value — the same pattern
/// <see cref="VBRuntimeArrayValue"/> uses for an array.
/// </summary>
/// <param name="ValueType">The variant's own <see cref="VBVarType"/> tag — the same COM <c>VARENUM</c>
/// value <c>VarType()</c> reports and OLE Automation marshals against.</param>
/// <param name="WrappedValue">The Variant's own wrapped value, cells/type info intact.</param>
public sealed record class VBRuntimeVariantValue(VBVarType ValueType, VBTypedValue WrappedValue) : IRuntimeValue
{
    /// <inheritdoc/>
    public object BoxedValue => WrappedValue.RuntimeValue.BoxedValue;
}
