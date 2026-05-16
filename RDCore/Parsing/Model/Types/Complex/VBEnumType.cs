using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.Parsing.Model.Types.Complex;

internal sealed record class VBEnumType : VBType, IVBMemberOwnerType, IVBDeclaredType
{
    public VBEnumType(string name, Symbol declaration, Symbol[]? definitions = null, IEnumerable<VBEnumMember>? members = null, bool isUserDefined = false)
        : base(typeof(int), name, isUserDefined)
    {
        Declaration = declaration;
        Definitions = definitions;

        Members = [.. (members ?? []).Cast<VBTypeMemberSymbol>()]; // NOTE: an enum without any members would not be compilable
    }

    public override VBType[] ConvertsSafelyToTypes =>
    [
        VBIntegerType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBDecimalType.TypeInfo,
        VBCurrencyType.TypeInfo,
        VBSingleType.TypeInfo,
        VBDoubleType.TypeInfo,
        VBStringType.TypeInfo,
        VBVariantType.TypeInfo
    ];

    private static readonly Lazy<VBLongValue> _defaultValue = new(() => VBLongValue.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public Symbol Declaration { get; init; }
    public Symbol[]? Definitions { get; init; }

    public ImmutableArray<VBTypeMemberSymbol> Members { get; init; }

    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members) => this with { Members = [.. members] };
}
