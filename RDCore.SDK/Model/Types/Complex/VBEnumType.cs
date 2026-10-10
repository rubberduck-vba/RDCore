using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
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

    /// <summary>
    /// A value of an enumeration type is a <c>Long</c> (<strong>MS-VBAL §5.2.3.4</strong>): what a variable declared as the enumeration holds is stored as one.
    /// </summary>
    public override VBTypedValue CreateValue(IBindingHandle handle) => new VBLongValue(handle);

    public ImmutableArray<VBTypeMemberSymbol> Members { get; init; }
    ImmutableArray<VBDeferredTypeMemberSymbol> IVBMemberOwnerType.DeferredMembers { get; init; } = [];
    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members) => this with { Members = [.. members] };
}
