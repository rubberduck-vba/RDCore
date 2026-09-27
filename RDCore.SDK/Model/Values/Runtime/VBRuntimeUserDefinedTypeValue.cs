using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Values.Runtime;

/// <summary>
/// Wraps a <see cref="VBUserDefinedTypeValue"/> for storage inside an <see cref="IRuntimeValue"/>, so the
/// UDT (its field cells included) round-trips through <c>ISessionStorage</c> like any other value.
/// </summary>
/// <remarks>
/// The same problem <see cref="VBRuntimeArrayValue"/> solves, and the same solution: a UDT's real content is
/// its fields, which a scalar <c>IRuntimeValue</c> — an <c>int</c>, a <see cref="VBRuntimeReference"/> — has
/// nowhere to hold, so reading the variable back would hand out a UDT with default fields however much had
/// been assigned to it.
/// <para>
/// 👉 Deliberately a plain reference type, not a <c>record</c>: <see cref="Abstract.VBTypedValue.Equals"/>
/// compares <c>Handle.Value.BoxedValue</c>, so boxing through a structurally-equatable wrapper would compare
/// two UDTs by calling back into <see cref="VBUserDefinedTypeValue"/>'s own equality — which reads through
/// this same boxed value — recursing forever. A plain class's reference equality breaks the cycle.
/// </para>
/// </remarks>
/// <param name="userDefinedType">The UDT value to wrap.</param>
public sealed class VBRuntimeUserDefinedTypeValue(VBUserDefinedTypeValue userDefinedType)
{
    /// <summary>
    /// The wrapped UDT, fields and all.
    /// </summary>
    public VBUserDefinedTypeValue UserDefinedType { get; } = userDefinedType;
}
