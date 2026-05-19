using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values;

/// <summary>
/// A special value that represents a VBType that is used in a value expression.
/// </summary>
/// <remarks>
/// Not used much beyond <c>TypeOf...Is</c> expressions.
/// </remarks>
public record class VBTypeDescValue : VBTypedValue
{
    /// <summary>
    /// Creates a new <c>VBTypeDescValue</c> describing the specified type at the specified symbol..
    /// </summary>
    /// <param name="typeInfo">The described type.</param>
    /// <param name="symbol">The symbol associated with this value.</param>
    public VBTypeDescValue(VBType typeInfo, Symbol symbol) : base(typeInfo, symbol) { }

    public override int Size => sizeof(int);
}