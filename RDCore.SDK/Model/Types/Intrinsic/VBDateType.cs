using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>Date</c> data type.
/// </summary>
public sealed record class VBDateType() : VBIntrinsicType<DateTime>(VBTypeNames.VBDate)
{
    private static readonly Lazy<VBDateType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Date</c> data type.
    /// </summary>
    public static VBDateType TypeInfo => _instance.Value;

    private static readonly Lazy<VBDateValue> _defaultValue = new(() => VBDateType.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    private static readonly Lazy<VBDateValue> _minValue = new(() 
        => new(GlobalSymbols.ExtensionSymbols.VBDateMinValue) { Value = new DateTime(100, 01, 01) }, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBDateValue MinValue => _minValue.Value;

    private static readonly Lazy<VBDateValue> _maxValue = new(() 
        => new(GlobalSymbols.ExtensionSymbols.VBDateMaxValue) { Value = new DateTime(9999, 12, 31, 23, 59, 59) }, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBDateValue MaxValue => _maxValue.Value;

    private static readonly Lazy<VBDateValue> _zero = new(() 
        => new(GlobalSymbols.ExtensionSymbols.VBDateZeroValue) { Value = new DateTime(1899, 12, 30) }, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBDateValue Zero => _zero.Value;

    public override int Size => sizeof(double);
}
