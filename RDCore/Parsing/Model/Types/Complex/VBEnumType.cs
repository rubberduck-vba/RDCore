using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;
using System.Collections.Immutable;

namespace RDCore.Parsing.Model.Types.Complex;

internal sealed record class VBEnumType : VBType, IVBMemberOwnerType, IVBDeclaredType
{
    public VBEnumType(string name, Symbol declaration, Symbol[]? definitions = null, IEnumerable<VBEnumMember>? members = null, bool isUserDefined = false)
        : base(typeof(int), name, isUserDefined)
    {
        Declaration = declaration;
        Definitions = definitions;

        Members = [.. (members ?? []).Cast<VBTypeMember>()]; // NOTE: an enum without any members would not be compilable
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

    public override VBTypedValue DefaultValue { get; } = VBLongValue.Zero;
    public Symbol Declaration { get; init; }
    public Symbol[]? Definitions { get; init; }

    public ImmutableArray<VBTypeMember> Members { get; init; }

    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMember> members) => this with { Members = [.. members] };
}
