using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types.Meta;

/// <summary>
/// An abstract meta-type representing any <c>VBTypeMemberSymbol</c>
/// </summary>
/// <param name="Name"></param>
public abstract record class VBMemberDesc(string Name) : VBType(typeof(Type), Name, isHidden: true)
{
    public override int Size => sizeof(int);

    private static readonly Lazy<VBTypedValue> _defaultValue = new(() => VBUnknownValue.DefaultValue, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}

/// <summary>
/// Describes a <em>deferred member</em>; an accepted but unresolved member of an existing or deferred module type.
/// </summary>
/// <remarks>
/// Encountering a <em>deferred member</em> dring semantic traversal attaches the required semantics to produce a <c>VBInferredTypeMember</c> that can be materialized into a code action.
/// </remarks>
/// <param name="Name">The name of the deferred member</param>
public record class VBDeferredMemberDesc(string Name) : VBMemberDesc(Name)
{
    private static readonly Lazy<VBDeferredMemberDesc> _instance = new(() => new(nameof(VBType)), LazyThreadSafetyMode.PublicationOnly);
    public static VBDeferredMemberDesc TypeInfo => _instance.Value;
}
