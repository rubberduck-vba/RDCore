#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
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

    private static readonly Lazy<VBByteValue> _defaultValue = new(() => new(GlobalSymbols.ExtensionSymbols.VBByteZeroValue), LazyThreadSafetyMode.PublicationOnly);
    public override VBByteValue DefaultValue => _defaultValue.Value;

    private static readonly Lazy<VBByteValue> _minValue = new(() => new VBByteValue(GlobalSymbols.ExtensionSymbols.VBByteMinValue) { ManagedValue = byte.MinValue }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBByteValue MinValue => _minValue.Value;
    public override double ManagedMinValue => _minValue.Value.ManagedValue;

    private static readonly Lazy<VBByteValue> _maxValue = new(() => new VBByteValue(GlobalSymbols.ExtensionSymbols.VBByteMaxValue) { ManagedValue = byte.MaxValue }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBByteValue MaxValue { get; } = _maxValue.Value;
    public override double ManagedMaxValue => _maxValue.Value.ManagedValue;

    private static readonly Lazy<VBByteValue> _zero = new(() => new VBByteValue(GlobalSymbols.ExtensionSymbols.VBByteZeroValue) { ManagedValue = 0 }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBByteValue Zero => _zero.Value;

    public override int Size => sizeof(byte);
}
