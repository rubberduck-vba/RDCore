using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types.Abstract;

internal sealed record class VBTypeDesc : VBType
{
    private static readonly VBTypeDesc _type = new(nameof(VBType));

    private VBTypeDesc(string name)
        : base(typeof(Type), name, isUserDefined: false, isHidden: true)
    {
    }

    public static VBTypeDesc TypeInfo => _type;
    public override VBTypedValue DefaultValue { get; } = new VBTypeDescValue(VBVariantType.TypeInfo);
}
