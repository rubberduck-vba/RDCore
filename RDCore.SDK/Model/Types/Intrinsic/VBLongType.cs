using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>Long</c> data type.
/// </summary>
public record class VBLongType() : VBNumericType<int>(Tokens.Long), IIntegralNumericType
{
    private static readonly Lazy<VBLongType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBLongType TypeInfo => _instance.Value;

    private static readonly Lazy<VBLongValue> _minValue = new(() 
        => new VBLongValue(GlobalSymbols.ExtensionSymbols.VBLongMinValue) { ManagedValue = int.MinValue }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBLongValue MinValue => _minValue.Value;

    private static readonly Lazy<VBLongValue> _maxValue = new(()
        => new VBLongValue(GlobalSymbols.ExtensionSymbols.VBLongMaxValue) { ManagedValue = int.MaxValue }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBLongValue MaxValue => _maxValue.Value;

    private static readonly Lazy<VBLongValue> _zero = new(()
        => new VBLongValue(GlobalSymbols.ExtensionSymbols.VBLongZeroValue) { ManagedValue = 0 }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBLongValue Zero => _zero.Value;

    public override VBTypedValue DefaultValue => VBLongType.Zero;
    public override int Size => sizeof(int);
}
