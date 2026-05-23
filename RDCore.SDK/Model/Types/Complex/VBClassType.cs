using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Unbound;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents a class type that can be consumed by VB code, not necessarily defined in user code.
/// </summary>
public record class VBClassType : VBType, IVBMemberOwnerType
{
    public VBClassType(VBClassModuleSymbol symbol, IEnumerable<VBTypeMemberSymbol>? members = null, bool isHidden = false)
        : base(typeof(object), symbol.Name, isHidden)
    {
        Symbol = symbol;
        Members = [.. members ?? []];
    }

    public override int Size => sizeof(int);

    public VBClassModuleSymbol Symbol { get; }

    /// <summary>
    /// An array of class types that this class directly inherits from, including interfaces.
    /// </summary>
    /// <remarks>
    /// Controlled by <c>Implements</c> instructions for user code.
    /// </remarks>
    public VBType[] Supertypes { get; init; } = [VBObjectType.TypeInfo];
    /// <summary>
    /// An array of class types that directly inherit from and can be safely converted to this class type.
    /// </summary>
    public VBType[] Subtypes { get; init; } = [];
    /// <summary>
    /// The default member of the class, if any.
    /// </summary>
    /// <remarks>
    /// Controlled by the <c>VB_DefaultMember</c> attribute or <c>@DefaultMember</c> annotation.
    /// </remarks>
    public VBTypeMemberSymbol? DefaultMember { get; init; }


    private readonly static Lazy<VBObjectValue> _defaultValue = new(() => VBObjectValue.Nothing, LazyThreadSafetyMode.PublicationOnly);
    public override VBObjectValue DefaultValue => _defaultValue.Value;

    public ImmutableArray<VBTypeMemberSymbol> Members { get; init; }
    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members) => this with { Members = [.. members] };
}
