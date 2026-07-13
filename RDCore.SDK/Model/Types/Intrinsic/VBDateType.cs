using RDCore.SDK.Model.Symbols;
#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBIntrinsicType{DateTime}"/> representing the <c>Date</c> data type.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>DateTime</c>.<br/>
/// 👉 <c>Date</c> values are also representable as a <c>double</c> via <c>SerialValue</c> (OLE Automation/OADate value).
/// </remarks>
public sealed record class VBDateType() : VBIntrinsicType<DateTime>(VBTypeNames.VBDate)
{
    private static readonly Lazy<VBDateType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Date</c> data type.
    /// </summary>
    public static VBDateType TypeInfo => _instance.Value;

    private static readonly Lazy<VBDateValue> _defaultValue = new(() => VBDateType.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    /// <summary>
    /// The minimum valid value for a <c>DateSerial</c>.
    /// </summary>
    public const double MinSerial = -657434;
    private static readonly Lazy<VBDateValue> _minValue = new(() 
        => new(GlobalSymbols.ExtensionSymbols.VBDateMinValue) { ManagedValue = new(new Values.Interop.ManagedInteropValue(new DateTime(100, 01, 01).ToOADate())) }, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBDateValue MinValue => _minValue.Value;

    /// <summary>
    /// The maximum valid value for a <c>DateSerial</c>.
    /// </summary>
    /// <remarks>
    /// Corresponds to <strong>9999-12-31T23:59:59Z</strong>
    /// </remarks>
    public const double MaxSerial = 2958465.999998843d;
    private static readonly Lazy<VBDateValue> _maxValue = new(() 
        => new(GlobalSymbols.ExtensionSymbols.VBDateMaxValue) { ManagedValue = new(new Values.Interop.ManagedInteropValue(MaxSerial)) }, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBDateValue MaxValue => _maxValue.Value;

    private static readonly Lazy<VBDateValue> _zero = new(() 
        => new(GlobalSymbols.ExtensionSymbols.VBDateZeroValue) { ManagedValue = new(new Values.Interop.ManagedInteropValue(new DateTime(1899, 12, 30).ToOADate())) }, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBDateValue Zero => _zero.Value;

    public override int Size => sizeof(double);
}
