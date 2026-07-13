#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Interop;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBNumericType{Decimal}"/> representing the <c>Currency</c> data type.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>decimal</c>.<br/>
/// 👉 Implements <see cref="IFixedPointNumericType"/>.
/// </remarks>
public record class VBCurrencyType() : VBNumericType<decimal>(VBTypeNames.VBCurrency), IFixedPointNumericType
{
    private static readonly Lazy<VBCurrencyType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Currency</c> data type.
    /// </summary>
    public static VBCurrencyType TypeInfo => _instance.Value;

    private static readonly Lazy<VBCurrencyValue> _defaultValue = new(() => VBCurrencyType.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    private static readonly Lazy<VBCurrencyValue> _minValue = new(() => (VBCurrencyValue)new VBCurrencyValue(GlobalSymbols.ExtensionSymbols.VBCurrencyMinValue).WithValue(long.MinValue * Math.Pow(10, -4)), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBCurrencyValue MinValue => _minValue.Value;
    public override double ManagedMinValue => Convert.ToDouble(ManagedInteropValue.CurrencyMinValue.Currency!.Value);

    private static readonly Lazy<VBCurrencyValue> _maxValue = new(() => (VBCurrencyValue)new VBCurrencyValue(GlobalSymbols.ExtensionSymbols.VBCurrencyMaxValue).WithValue(long.MaxValue * Math.Pow(10, -4)), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBCurrencyValue MaxValue => _maxValue.Value;
    public override double ManagedMaxValue => Convert.ToDouble(ManagedInteropValue.CurrencyMaxValue.Currency!.Value);

    private static readonly Lazy<VBCurrencyValue> _zero = new(() => (VBCurrencyValue)new VBCurrencyValue(GlobalSymbols.ExtensionSymbols.VBCurrencyZeroValue).WithValue(0d), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBCurrencyValue Zero => _zero.Value;


    public override int Size => sizeof(decimal);
}
