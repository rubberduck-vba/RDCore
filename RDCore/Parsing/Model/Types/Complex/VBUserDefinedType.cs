using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;
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

        Members = [.. (members ?? []).OfType<VBTypeMember>()];
    }

    public override VBType[] ConvertsSafelyToTypes { get; } = [VBVariantType.TypeInfo];
    public override VBTypedValue DefaultValue { get; }

    public override bool CanPassByValue { get; } = false;

    public Symbol Declaration { get; init; }
    public Symbol[]? Definitions { get; init; }

    public ImmutableArray<VBTypeMember> Members { get; init; }
    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMember> members) => this with { Members = [.. members] };
}
