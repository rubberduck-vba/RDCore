using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Meta;

/// <summary>
/// An abstract meta-type representing any <c>VBTypeMemberSymbol</c>
/// </summary>
/// <param name="Name">The <em>identifier name</em> of the member.</param>
public abstract record class VBMemberDesc(string Name, ImmutableArray<VBParameterSymbol> Parameters) : VBType(typeof(Type), Name, isHidden: true)
{
    public override int Size => sizeof(int);

    // NOTE: a value of this type is VBUnknown until determined with name resolution semantics.
    private static readonly Lazy<VBTypedValue> _defaultValue = new(() => VBUnknownValue.DefaultValue, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}

/// <summary>
/// Describes a <em>deferred member</em>; an accepted but unresolved member of an existing or deferred module type.
/// </summary>
/// <remarks>
/// Encountering a <em>deferred member</em> dring semantic traversal attaches the required semantics to produce a <c>VBInferredTypeMember</c> that can be materialized into a code action.
/// </remarks>
/// <param name="Name">The <em>identifier name</em> of the deferred member</param>
public record class VBDeferredMemberDesc(string Name, ImmutableArray<VBParameterSymbol> Parameters) : VBMemberDesc(Name, Parameters)
{
    private static readonly Lazy<VBDeferredMemberDesc> _instance = new(() => new(nameof(VBType), []), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Describes a specific <em>deferred</em> type member.
    /// </summary>
    /// <remarks>
    /// 👉 The module owning this member may <em>also</em> be deferred.
    /// </remarks>
    public static VBDeferredMemberDesc TypeInfo => _instance.Value;
}
