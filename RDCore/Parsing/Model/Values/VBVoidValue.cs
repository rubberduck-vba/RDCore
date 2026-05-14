using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal sealed record class VBVoidValue : VBTypedValue
{
    public static VBVoidValue Void { get; } = new();

    private VBVoidValue() : base(VBVoidType.TypeInfo) { }
    public override int Size => 0;
}