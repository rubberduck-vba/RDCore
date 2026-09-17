using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Complex;

/// <summary>
/// Represents a class type that can be consumed by VB code, not necessarily defined in user code.
/// </summary>
public record class VBClassType(VBClassModuleSymbol Symbol, ImmutableArray<VBTypeMemberSymbol> Members, bool IsHidden = false)
    : VBType(typeof(object), Symbol.Name, IsHidden), IVBMemberOwnerType
{
    /// <summary>
    /// Builds the class's own default interface — its <c>Public</c>, implicitly-public, and
    /// <c>Friend</c> members, the surface visible to a member-access expression typed against this
    /// class, whether the reference is <c>Me</c> or any other variable declared as this class.
    /// <c>Friend</c> counts as visible here because RDCore only ever composes one project's modules
    /// at a time (no referenced-project/library symbol provider exists yet) — anything resolving
    /// against a given composition is, by construction, same-project code, and MS-VBAL's <c>Friend</c>
    /// accessibility is visible project-wide, not just module-wide. <c>Private</c> members are reached,
    /// from inside the class's own code, only through ordinary lexical name resolution — never through
    /// member access — so they alone are excluded from <see cref="Members"/> here.
    /// </summary>
    /// <remarks>
    /// Called exactly once per class, by <c>WorkspaceSymbolResolver.Compose</c>, to populate
    /// <see cref="VBClassModuleSymbol.DefaultInterfaceMembers"/> — not at resolution time.
    /// </remarks>
    public static VBClassType FromClassModule(VBClassModuleSymbol classModule)
        => new(classModule, [.. classModule.Members.Where(member => member.AccessModifier is AccessModifier.Public or AccessModifier.Implicit or AccessModifier.Friend)]);

    /// <summary>
    /// An array of class types that this class directly inherits from, including interfaces.
    /// </summary>
    /// <remarks>
    /// Controlled by <c>Implements</c> instructions for user code.
    /// </remarks>
    public VBType[] Supertypes { get; init; } = [VBObjectType.TypeInfo];
    /// <summary>
    /// The default member of the class, if any.
    /// </summary>
    /// <remarks>
    /// Controlled by the <c>VB_UserMemId</c> attribute or <c>@DefaultMember</c> annotation.
    /// </remarks>
    public VBTypeMemberSymbol? DefaultMember { get; init; }

    private readonly static Lazy<VBObjectValue> _defaultValue = new(() => VBObjectValue.Nothing, LazyThreadSafetyMode.PublicationOnly);
    public override VBObjectValue DefaultValue => _defaultValue.Value;

    ImmutableArray<VBDeferredTypeMemberSymbol> IVBMemberOwnerType.DeferredMembers { get; init; } = [];

    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members) => this with { Members = [.. members] };
}

public record class VBDeferredClassType(string Name, Uri Uri): VBDeferredType(Name, Uri)
{
    private static readonly Lazy<VBObjectValue> _defaultValue = new(() => VBNothingValue.Nothing, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}