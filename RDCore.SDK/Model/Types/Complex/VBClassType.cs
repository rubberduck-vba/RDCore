using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Complex;

/// <summary>
/// Represents a class type that can be consumed by VB code, not necessarily defined in user code.
/// </summary>
public record class VBClassType : VBType, IVBMemberOwnerType
{
    public VBClassType(VBClassModuleSymbol symbol, bool isUserDefined = false, IEnumerable<VBTypeMemberSymbol>? members = null, bool isHidden = false)
        : base(typeof(object), symbol.Name, isUserDefined, isHidden)
    {
        Symbol = symbol;
        Members = [.. members ?? []];
    }

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
    /// <summary>
    /// <c>true</c> if this class type is used as an interface (i.e., other classes implement it).
    /// </summary>
    /// ...or if it is marked with an @Interface annotation?
    public bool IsInterface => Subtypes.Length != 0; // || @Interface annotation?
    /// <summary>
    /// Whether <c>new</c> instances of this class type can be created outside the project the class is defined in.
    /// </summary>
    public bool IsCreatable { get; init; }

    public override VBType[] ConvertsSafelyToTypes => [.. Supertypes, VBVariantType.TypeInfo];

    private readonly static Lazy<VBObjectValue> _defaultValue = new(() => VBObjectValue.Nothing, LazyThreadSafetyMode.PublicationOnly);
    public override VBObjectValue DefaultValue => _defaultValue.Value;

    public ImmutableArray<VBTypeMemberSymbol> Members { get; init; }
    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members) => this with { Members = [.. members] };
}
