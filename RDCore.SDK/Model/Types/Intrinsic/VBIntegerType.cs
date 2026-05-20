using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>Integer</c> data type.
/// </summary>
public sealed record class VBIntegerType() : VBNumericType<short>(Tokens.Integer), IIntegralNumericType
{
    private static readonly Lazy<VBIntegerType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBIntegerType TypeInfo => _instance.Value;

    private static readonly Lazy<VBIntegerValue> _defaultValue = new(() => VBIntegerType.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    private static readonly Lazy<VBIntegerValue> _minValue = new(() 
        => new VBIntegerValue(GlobalSymbols.ExtensionSymbols.VBIntegerMinValue) { ManagedValue = short.MinValue }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBIntegerValue MinValue => _minValue.Value;

    private static readonly Lazy<VBIntegerValue> _maxValue = new(()
        => new VBIntegerValue(GlobalSymbols.ExtensionSymbols.VBIntegerMaxValue) { ManagedValue = short.MaxValue }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBIntegerValue MaxValue => _maxValue.Value;

    private static readonly Lazy<VBIntegerValue> _zero = new(() 
        => new VBIntegerValue(GlobalSymbols.ExtensionSymbols.VBIntegerZeroValue) { ManagedValue = 0 }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBIntegerValue Zero => _zero.Value;

    public override int Size => sizeof(short);
}
