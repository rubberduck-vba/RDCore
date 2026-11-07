using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Values.Runtime;

/// <summary>
/// Wraps a <see cref="VBArrayValue"/> for storage inside an <see cref="IRuntimeValue"/>, so the array
/// (its element cells included) round-trips through <c>ISessionStorage</c> like any other value.
/// </summary>
/// <remarks>
/// 👉 Deliberately a plain reference type, not a <c>record</c>: <see cref="VBTypedValue.Equals"/>
/// compares <c>Handle.Value.BoxedValue</c>, so boxing the array through a structurally-equatable
/// wrapper would compare two arrays by calling back into <see cref="VBArrayValue"/>'s own (inherited,
/// structural) equality — which itself reads through this same boxed value — recursing forever. A
/// plain class's default (reference) equality breaks that cycle.
/// </remarks>
public sealed class VBRuntimeArrayValue(VBArrayValue array)
{
    /// <summary>
    /// The wrapped array, cells and all.
    /// </summary>
    public VBArrayValue Array { get; } = array;
}
