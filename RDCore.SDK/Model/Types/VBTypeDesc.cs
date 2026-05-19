using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Types;

public sealed record class VBTypeDesc : VBType
{
    private static readonly Lazy<VBTypeDesc> _instance = new(() => new(nameof(VBType)), LazyThreadSafetyMode.PublicationOnly);
    public static VBTypeDesc TypeInfo => _instance.Value;

    private static readonly Lazy<VBTypeDescValue> _defaultValue = new(() => new VBTypeDescValue(VBVariantType.TypeInfo, GlobalSymbols.TypeDesc), LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    private VBTypeDesc(string name) 
        : base(typeof(Type), name, isUserDefined: false, isHidden: true)
    {
    }
}
