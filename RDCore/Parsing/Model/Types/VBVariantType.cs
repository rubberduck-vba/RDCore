using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBVariantType : VBIntrinsicType<object?>
{
    private static readonly VBVariantType _type = new();

    protected VBVariantType(VBType? subtype = null) : base(Tokens.Variant)
    {
        Subtype = subtype ?? VBEmptyType.TypeInfo;
    }

    public VBType Subtype { get; init; }
    public bool IsEmpty => Subtype is VBEmptyType;

    public static VBVariantType TypeInfo => _type;

    public override bool RuntimeBinding { get; } = true;
    public override VBVariantValue DefaultValue => new(Subtype?.DefaultValue ?? VBEmptyValue.Empty);
    public override VBType[] ConvertsSafelyToTypes { get; } = [];

    public override string? DefToken => Tokens.DefVar;
}
