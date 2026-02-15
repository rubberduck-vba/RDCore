using RDCore.Parsing.Model.Types.Abstract;

namespace RDCore.Parsing.Model.Values;

/// <summary>
/// A special value that represents a VBType that is used in a value expression.
/// </summary>
/// <remarks>
/// Not used much beyond <c>TypeOf...Is</c> expressions.
/// </remarks>
internal record class VBTypeDescValue : VBTypedValue
{
    public VBTypeDescValue(VBType symbol) : base(symbol) { }

    public override int Size => sizeof(int);
}