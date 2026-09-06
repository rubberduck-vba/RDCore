#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBNumericType{Double}"/> representing the <c>Double</c> data type.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>double</c>.<br/>
/// 👉 Implements <see cref="IFloatingPointNumericType"/>.
/// </remarks>
public sealed record class VBDoubleType() : VBNumericType<double>(VBTypeNames.VBDouble), IFloatingPointNumericType
{
    /// <summary>
    /// The number of significant digits retained in a String representation of a value of this type.
    /// </summary>
    public const int SignificantIntegerDigits = 15;

    private static readonly Lazy<VBDoubleValue> _minValue = new(() => new(double.MinValue), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBDoubleValue MinValue => _minValue.Value;
    public override double ManagedMinValue => Convert.ToDouble(_minValue.Value.RuntimeValue.BoxedValue);

    private static readonly Lazy<VBDoubleValue> _maxValue = new(() => new(double.MaxValue), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBDoubleValue MaxValue => _maxValue.Value;
    public override double ManagedMaxValue => Convert.ToDouble(_maxValue.Value.RuntimeValue.BoxedValue);

    private static readonly Lazy<VBDoubleValue> _zero = new(() => new(0d), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBDoubleValue Zero => _zero.Value;
    private static readonly Lazy<VBDoubleValue> _one = new(() => new(1d), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>1</c> (one) representation of this data type.
    /// </summary>
    /// <remarks>
    /// Used for returning a constant 1 from certain runtime semantics.
    /// </remarks>
    public static VBDoubleValue One => _one.Value;

    private static readonly Lazy<VBDoubleValue> _defaultValue = new(() => VBDoubleType.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    private static readonly Lazy<VBDoubleType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Double</c> data type.
    /// </summary>
    public static VBDoubleType TypeInfo => _instance.Value;


    public override VBNumericTypedValue CreateValue(double value) => new VBDoubleValue(value);
    public override VBTypedValue CreateValue(RDCore.SDK.Model.Values.Bindings.IBindingHandle handle) => new VBDoubleValue(handle);
}
