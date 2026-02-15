using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;
using System.Collections.Immutable;

namespace RDCore.Parsing.Model.Types.Complex;

/// <summary>
/// Represents a class type that can be consumed by VB code, not necessarily defined in user code.
/// </summary>
internal record class VBClassType : VBType, IVBMemberOwnerType
{
    public VBClassType(string name, bool isUserDefined = false, IEnumerable<VBTypeMember>? members = null, bool isHidden = false)
        : base(typeof(object), name, isUserDefined, isHidden)
    {
        Members = [.. members ?? []];
    }

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
    public VBTypeMember? DefaultMember { get; init; }
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
    public override VBObjectValue DefaultValue { get; } = VBObjectValue.Nothing;

    public ImmutableArray<VBTypeMember> Members { get; init; }
    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMember> members) => this with { Members = [.. members] };
}
