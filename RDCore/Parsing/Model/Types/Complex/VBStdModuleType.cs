using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;
using RDCore.Parsing.Model.Values.Abstract;
using System.Collections.Immutable;

namespace RDCore.Parsing.Model.Types.Complex;

internal record class VBStdModuleType : VBType, IVBMemberOwnerType
{
    public VBStdModuleType(string name, bool isUserDefined = true, IEnumerable<VBTypeMemberSymbol>? members = null, bool isHidden = false)
        : base(typeof(object), name, isUserDefined, isHidden)
    {
        Members = [.. members ?? []];
    }

    private static readonly Lazy<VBVoidValue> _defaultValue = new(() => VBVoidValue.Void, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public ImmutableArray<VBTypeMemberSymbol> Members { get; init; }

    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members) => this with { Members = [.. members] };
}