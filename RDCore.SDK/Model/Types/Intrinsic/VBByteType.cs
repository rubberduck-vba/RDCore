#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Interop;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBNumericType{Byte}"/> representing the <c>Byte</c> data type.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>byte</c>.
/// 👉 Implements <see cref="IIntegralNumericType"/>.
/// </remarks>
public sealed record class VBByteType() : VBNumericType<byte>(VBTypeNames.VBByte), IIntegralNumericType
{
    private static readonly Lazy<VBByteType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Byte</c> data type.
    /// </summary>
    public static VBByteType TypeInfo => _instance.Value;

    private static readonly Lazy<VBByteValue> _defaultValue = new(() => (VBByteValue)new VBByteValue(GlobalSymbols.ExtensionSymbols.VBByteZeroValue).WithValue(ManagedInteropValue<byte>.ByteZeroValue.Value), LazyThreadSafetyMode.PublicationOnly);
    public override VBByteValue DefaultValue => _defaultValue.Value;

    private static readonly Lazy<VBByteValue> _minValue = new(() => (VBByteValue)new VBByteValue(GlobalSymbols.ExtensionSymbols.VBByteMinValue).WithValue(ManagedInteropValue<byte>.ByteMinValue.Value), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBByteValue MinValue => _minValue.Value;
    public override double ManagedMinValue => (double)_minValue.Value.ManagedValue.InteropValue!.BoxedValue;

    private static readonly Lazy<VBByteValue> _maxValue = new(() => (VBByteValue)new VBByteValue(GlobalSymbols.ExtensionSymbols.VBByteMaxValue).WithValue(ManagedInteropValue<byte>.ByteMaxValue.Value), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBByteValue MaxValue { get; } = _maxValue.Value;
    public override double ManagedMaxValue => (double)_maxValue.Value.ManagedValue.InteropValue!.BoxedValue;

    private static readonly Lazy<VBByteValue> _zero = new(() => (VBByteValue)new VBByteValue(GlobalSymbols.ExtensionSymbols.VBByteZeroValue).WithValue(ManagedInteropValue<byte>.ByteZeroValue.Value), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBByteValue Zero => _zero.Value;

    public override int Size => sizeof(byte);
}
