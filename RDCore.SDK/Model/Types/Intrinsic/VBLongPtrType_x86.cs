#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBIntrinsicType{Int32}"/> representing the <c>LongPtr</c> data type <em>in the context of a <strong>32-bit</strong> execution environment</em>.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>int</c>.<br/>
/// </remarks>
public record class VBLongPtrType_x86() : VBIntrinsicType<int>(VBTypeNames.VBLongPtr)
{
    public static int BitnessAwarePtrSize => sizeof(int);

    private static readonly Lazy<VBLongPtrType_x86> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>LongPtr</c> data type (32-bit).
    /// </summary>
    public static VBLongPtrType_x86 TypeInfo => _instance.Value;

    private static readonly Lazy<VBLongPtrValue> _defaultValue = new(() => VBLongPtrType_x86.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    private static readonly Lazy<VBLongPtrValue> _minValue = new(() 
        => new VBLongPtrValue(Is64Bit: false, GlobalSymbols.ExtensionSymbols.VBLongPtrMinValue) { ManagedValue = int.MinValue }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBLongPtrValue MinValue => _minValue.Value;

    private static readonly Lazy<VBLongPtrValue> _maxValue = new(() 
        => new VBLongPtrValue(Is64Bit: false, GlobalSymbols.ExtensionSymbols.VBLongPtrMaxValue) { ManagedValue = int.MaxValue }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBLongPtrValue MaxValue => _maxValue.Value;

    private static readonly Lazy<VBLongPtrValue> _zero = new(() 
        => new VBLongPtrValue(Is64Bit: false, GlobalSymbols.ExtensionSymbols.VBLongPtrZeroValue) { ManagedValue = 0 }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBLongPtrValue Zero => _zero.Value;

    /// <summary>
    /// The size of a <c>VBLongPtrValue</c> depends on the bitness-aware pointer size.
    /// </summary>
    public override int Size => BitnessAwarePtrSize;
}
