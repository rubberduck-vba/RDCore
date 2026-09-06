#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBIntrinsicType{Int64}"/> representing the <c>LongPtr</c> data type <em>in the context of a <strong>64-bit</strong> execution environment</em>.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>long</c>.<br/>
/// </remarks>
public record class VBLongPtrType_x64() : VBIntrinsicType<long>(VBTypeNames.VBLongPtr)
{
    public static int BitnessAwarePtrSize => sizeof(Int64); // safe: type is necessarily x64

    private static readonly Lazy<VBLongPtrType_x64> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>LongPtr</c> data type (64-bit).
    /// </summary>
    public static VBLongPtrType_x64 TypeInfo => _instance.Value;

    private static readonly Lazy<VBLongPtrValue> _defaultValue = new(() => VBLongPtrType_x64.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    private static readonly Lazy<VBLongPtrValue> _minValue = new(()
        => new VBLongPtrValue(Int64.MinValue), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBLongPtrValue MinValue => _minValue.Value;

    private static readonly Lazy<VBLongPtrValue> _maxValue = new(()
        => new VBLongPtrValue(Int64.MaxValue), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBLongPtrValue MaxValue => _maxValue.Value;

    private static readonly Lazy<VBLongPtrValue> _zero = new(()
        => new VBLongPtrValue(0L), LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBLongPtrValue Zero => _zero.Value;

    public override VBTypedValue CreateValue(RDCore.SDK.Model.Values.Bindings.IBindingHandle handle) => new VBLongPtrValue(handle);
}
