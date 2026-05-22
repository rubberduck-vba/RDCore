using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>LongLong</c> data type.
/// </summary>
/// <remarks>
/// This type is statically invalid in a 32-bit environment.
/// </remarks>
public record class VBLongLongType() : VBNumericType<long>(VBTypeNames.VBLong), IIntegralNumericType
{
    private static readonly Lazy<VBLongLongType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>LongLong</c> data type.
    /// </summary>
    /// <remarks>
    /// This type is statically invalid in a 32-bit environment.
    /// </remarks>
    public static VBLongLongType TypeInfo => _instance.Value;

    private static readonly Lazy<VBLongLongValue> _defaultValue = new(() => VBLongLongType.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    private static readonly Lazy<VBLongLongValue> _minValue = new(() => new VBLongLongValue(GlobalSymbols.ExtensionSymbols.VBLongLongMinValue) { ManagedValue = long.MinValue }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBLongLongValue MinValue => _minValue.Value;

    private static readonly Lazy<VBLongLongValue> _maxValue = new(() => new VBLongLongValue(GlobalSymbols.ExtensionSymbols.VBLongLongMaxValue) { ManagedValue = long.MaxValue }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBLongLongValue MaxValue => _maxValue.Value;

    private static readonly Lazy<VBLongLongValue> _zeroValue = new(() => new VBLongLongValue(GlobalSymbols.ExtensionSymbols.VBLongLongZeroValue) { ManagedValue = 0 }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBLongLongValue Zero => _zeroValue.Value;

    public override int Size => sizeof(long);
}
