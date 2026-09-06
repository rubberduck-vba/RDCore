#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBNumericType{Int16}"/> representing the <c>Integer</c> data type.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>short</c>.<br/>
/// 👉 Implements <see cref="IIntegralNumericType"/>.
/// </remarks>
public sealed record class VBIntegerType() : VBNumericType<short>(VBTypeNames.VBInteger), IIntegralNumericType
{
    private static readonly Lazy<VBIntegerType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Integer</c> data type.
    /// </summary>
    public static VBIntegerType TypeInfo => _instance.Value;

    private static readonly Lazy<VBIntegerValue> _defaultValue = new(() => VBIntegerType.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    private static readonly Lazy<VBIntegerValue> _minValue = new(() 
        => new VBIntegerValue(short.MinValue), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBIntegerValue MinValue => _minValue.Value;
    public override double ManagedMinValue => (double)_minValue.Value.UnderlyingValue.RuntimeValue!.BoxedValue;

    private static readonly Lazy<VBIntegerValue> _maxValue = new(()
        => new VBIntegerValue(short.MaxValue), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBIntegerValue MaxValue => _maxValue.Value;
    public override double ManagedMaxValue => (double)_maxValue.Value.UnderlyingValue.RuntimeValue!.BoxedValue;

    private static readonly Lazy<VBIntegerValue> _zero = new(() 
        => new VBIntegerValue(0), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBIntegerValue Zero => _zero.Value;

    private static readonly Lazy<VBIntegerValue> _negativeOne = new(() 
        => new VBIntegerValue(-1), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>-1</c> (negative one) representation of this data type.
    /// </summary>
    public static VBIntegerValue NegativeOne => _negativeOne.Value;

}
