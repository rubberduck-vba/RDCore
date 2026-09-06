#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBNumericType{Int32}"/> representing the <c>Long</c> data type.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>int</c>.<br/>
/// 👉 Implements <see cref="IIntegralNumericType"/>.
/// </remarks>
public record class VBLongType() : VBNumericType<int>(VBTypeNames.VBLong), IIntegralNumericType
{
    private static readonly Lazy<VBLongType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Long</c> data type.
    /// </summary>
    public static VBLongType TypeInfo => _instance.Value;

    private static readonly Lazy<VBLongValue> _minValue = new(() 
        => new VBLongValue(int.MinValue), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBLongValue MinValue => _minValue.Value;
    public override double ManagedMinValue => (double)_minValue.Value.RuntimeValue.BoxedValue;

    private static readonly Lazy<VBLongValue> _maxValue = new(()
        => new VBLongValue(int.MaxValue), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBLongValue MaxValue => _maxValue.Value;
    public override double ManagedMaxValue => (double)_maxValue.Value.RuntimeValue.BoxedValue;

    private static readonly Lazy<VBLongValue> _zero = new(()
        => new VBLongValue(0), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBLongValue Zero => _zero.Value;

    public override VBTypedValue DefaultValue => VBLongType.Zero;

    public override VBNumericTypedValue CreateValue(double value) => new VBLongValue(Convert.ToInt32(value));
    public override VBTypedValue CreateValue(RDCore.SDK.Model.Values.Bindings.IBindingHandle handle) => new VBLongValue(handle);
}
