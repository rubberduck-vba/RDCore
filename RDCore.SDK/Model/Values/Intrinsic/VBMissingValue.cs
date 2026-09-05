using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Variant/Missing</c> value.
/// </summary>
public sealed record class VBMissingValue() : VBTypedValue(VBMissingType.TypeInfo)
{
    // for construction uniformity; a missing optional argument has no binding.
    public VBMissingValue(IBindingHandle handle) : this() { Handle = handle; }

    private static readonly Lazy<VBMissingValue> _missing = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBMissingValue Missing => _missing.Value;

    public override int Size => sizeof(int);
}