using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.Parsing.Model.Types.Complex;

internal sealed record class VBUserDefinedType : VBType, IVBDeclaredType, IVBMemberOwnerType
{
    public VBUserDefinedType(string name, Symbol declaration, Symbol[]? definitions = null, IEnumerable<VBUserDefinedTypeMember>? members = null)
        : base(typeof(object), name, isUserDefined: true)
    {
        DefaultValue = new VBUserDefinedTypeValue(this, declaration);
        Declaration = declaration;
        Definitions = definitions;

        Members = [.. (members ?? []).OfType<VBTypeMemberSymbol>()];
    }

    public override VBType[] ConvertsSafelyToTypes { get; } = [VBVariantType.TypeInfo];
    public override VBTypedValue DefaultValue { get; }

    public override bool CanPassByValue { get; } = false;

    public Symbol Declaration { get; init; }
    public Symbol[]? Definitions { get; init; }

    public ImmutableArray<VBTypeMemberSymbol> Members { get; init; }
    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members) => this with { Members = [.. members] };
}
