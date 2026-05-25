using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Values;

/// <summary>
/// Represents a precompiler constant value; treated as an <c>Integer</c>.
/// </summary>
public sealed record class PrecompilerConstantValue : VBIntegerValue
{
    /// <summary>
    /// Creates a new precompiler constant value.
    /// </summary>
    /// <param name="symbol">The symbol associated with this value.</param>
    /// <param name="managedValue">The underlying managed value of this constant.</param>
    public PrecompilerConstantValue(Symbol symbol, int managedValue)
        : base(symbol)
    {
        ManagedValue = managedValue;
    }
}
