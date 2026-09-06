#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBNumericType{Int64}"/> representing the <c>LongLong</c> data type.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>long</c>.<br/>
/// 💥 Declarations of this data type are <strong>statically invalid</strong> in a <strong>32-bit</strong> environment.<br/>
/// 👉 Implements <see cref="IIntegralNumericType"/>.<br/>
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

    private static readonly Lazy<VBLongLongValue> _minValue = new(() => new VBLongLongValue(long.MinValue), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBLongLongValue MinValue => _minValue.Value;
    public override double ManagedMinValue => ((VBRuntimeValue<double>)_minValue.Value.UnderlyingValue.RuntimeValue!).StoredValue;

    private static readonly Lazy<VBLongLongValue> _maxValue = new(() => new VBLongLongValue(long.MaxValue), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBLongLongValue MaxValue => _maxValue.Value;
    public override double ManagedMaxValue => ((VBRuntimeValue<double>)_maxValue.Value.UnderlyingValue.RuntimeValue!).StoredValue;

    private static readonly Lazy<VBLongLongValue> _zeroValue = new(() => new VBLongLongValue(0L), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBLongLongValue Zero => _zeroValue.Value;

}
