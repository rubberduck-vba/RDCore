using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values;

/// <summary>
/// Represents the data type returned by non-returning member procedures.
/// </summary>
/// <remarks>
/// This data type has no attached or specified semantics.
/// </remarks>
public sealed record class VBVoidValue() : VBTypedValue(VBVoidType.TypeInfo, GlobalSymbols.StaticSymbols.VBVoid)
{
    private static readonly Lazy<VBVoidValue> _void = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBVoidValue Void => _void.Value;

    public override int Size => 0;
}