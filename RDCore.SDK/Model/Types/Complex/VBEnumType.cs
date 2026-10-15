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

    /// <summary>
    /// The declared type <paramref name="type"/> gives a declaration whose <c>As</c> clause names it: <c>Long</c> for an enumeration (<strong>MS-VBAL
    /// §5.2.3.4</strong>: "the meaning is the same as if the enum name was replaced with the declared type Long"), and any other type itself.
    /// </summary>
    /// <remarks>
    /// So an enumeration and <c>Long</c>, or two enumerations, are the same declared type where two declared types are matched: an event handler's
    /// parameters and its event's (§5.3.1.8), an implemented name declaration's and its interface member's (§5.3.1.9), and a <c>ByRef</c> parameter and
    /// the variable it is given (§5.3.1.11), by a call or by a <c>RaiseEvent</c> (§5.4.2.20). An <c>Integer</c> and a <c>Long</c> are not. Only the type
    /// itself is mapped, not an array's element type: an array of an enumeration is not an array of <c>Long</c> here.
    /// </remarks>
    /// <param name="type">The type an <c>As</c> clause names.</param>
    public static VBType AsDeclaredType(VBType type) => type is VBEnumType ? VBLongType.TypeInfo : type;

    public ImmutableArray<VBTypeMemberSymbol> Members { get; init; }
    ImmutableArray<VBDeferredTypeMemberSymbol> IVBMemberOwnerType.DeferredMembers { get; init; } = [];
    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members) => this with { Members = [.. members] };
}
