using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Complex;

/// <summary>
/// Represents any <c>Enum</c> type.
/// </summary>
public sealed record class VBEnumType(Symbol Symbol, bool IsHidden = false) : VBType(typeof(Type), Symbol.Name, IsHidden), IVBMemberOwnerType
{
    public VBEnumType(Symbol symbol, IEnumerable<VBEnumConstMemberSymbol>? members = null, bool isHidden = false)
        : this(symbol, isHidden)
    {
        Members = [.. (members ?? []).Cast<VBTypeMemberSymbol>()]; // NOTE: an enum without any members would not be statically compilable MS-VBA
    }

    private static readonly Lazy<VBLongValue> _defaultValue = new(() => VBLongType.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int);

    public ImmutableArray<VBTypeMemberSymbol> Members { get; init; }
    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members) => this with { Members = [.. members] };
}
