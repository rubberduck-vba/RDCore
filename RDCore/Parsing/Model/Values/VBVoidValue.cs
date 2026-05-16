using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values.Abstract;

namespace RDCore.Parsing.Model.Values;

internal sealed record class VBVoidValue() : VBTypedValue(VBVoidType.TypeInfo)
{
    public static VBVoidValue Void { get; } = new();

    public override int Size => 0;
}