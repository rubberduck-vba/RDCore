using RDCore.SDK.Model.Symbols;

namespace RDCore.Runtime.LanguageCore.StdLib.Abstract;

/// <summary>
/// States that a standard-library parameter declared <c>Variant</c> reads the value of its argument, as MS-VBA compiles it: an object given to it stands for
/// its default member's value, and one that has none to give is a compile error (<see cref="ValueDestination"/>).
/// </summary>
/// <remarks>
/// MS-VBAL declares these parameters <c>Variant</c>, as it declares those of <c>Round</c> or <c>Hex</c>, which MS-VBA compiles any argument for. What tells
/// them apart is only how MS-VBA compiles a call (recorded in Excel): <c>CInt(Nothing)</c> is "Invalid use of object", <c>Round(Nothing)</c> compiles.
/// The symbol of the parameter carries it as <see cref="Model.Symbols.Abstract.SymbolProperties.ValueDestination"/>.
/// </remarks>
/// <param name="destination">What the parameter expects of an object: a value of a declared type, unless it says otherwise.</param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class StdLibValueAttribute(ValueDestination destination = ValueDestination.TypedValue) : Attribute
{
    /// <summary>
    /// What the parameter expects of an object.
    /// </summary>
    public ValueDestination Destination { get; } = destination;
}
