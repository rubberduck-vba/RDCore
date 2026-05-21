using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>Currency</c> data type.
/// </summary>
public record class VBCurrencyType() : VBNumericType<decimal>(Tokens.Currency), IFixedPointNumericType
{
    private static readonly Lazy<VBCurrencyType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBCurrencyType TypeInfo => _instance.Value;

    private static readonly Lazy<VBCurrencyValue> _defaultValue = new(() => VBCurrencyType.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    private static readonly Lazy<VBCurrencyValue> _minValue = new(() => new VBCurrencyValue(GlobalSymbols.ExtensionSymbols.VBCurrencyMinValue) { ManagedValue = (double)(long.MinValue * Math.Pow(10, -4)) }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBCurrencyValue MinValue => _minValue.Value;

    private static readonly Lazy<VBCurrencyValue> _maxValue = new(() => new VBCurrencyValue(GlobalSymbols.ExtensionSymbols.VBCurrencyMaxValue) { ManagedValue = (double)(long.MaxValue * Math.Pow(10, -4)) }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBCurrencyValue MaxValue => _maxValue.Value;

    private static readonly Lazy<VBCurrencyValue> _zero = new(() => new VBCurrencyValue(GlobalSymbols.ExtensionSymbols.VBCurrencyZeroValue) { ManagedValue = (double)0 }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBCurrencyValue Zero => _zero.Value;


    public override int Size => sizeof(decimal);
}
