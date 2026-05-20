using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types.Meta;

/// <summary>
/// An abstract meta-type representing any <c>VBPropertyLetMemberSymbol</c>
/// </summary>
/// <param name="Name">The name of the member</param>
public record class VBPropertyLetProcedureDesc(string Name) : VBProcedureMemberDesc(Name)
{
    private static readonly Lazy<VBPropertyLetProcedureDesc> _instance = new(() => new(nameof(VBType)), LazyThreadSafetyMode.PublicationOnly);
    public static new VBPropertyLetProcedureDesc TypeInfo => _instance.Value;

    private static readonly Lazy<VBTypedValue> _defaultValue = new(() => VBUnknownValue.DefaultValue, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}

/// <summary>
/// Describes a <em>deferred property (Let) member</em>; an accepted but unresolved member of an existing or deferred module type.
/// </summary>
/// <remarks>
/// Encountering a <em>deferred member</em> dring semantic traversal attaches the required semantics to produce a <c>VBInferredTypeMember</c> that can be materialized into a code action.
/// </remarks>
/// <param name="Name">The name of the deferred member</param>
public sealed record class VBDeferredPropertyLetProcedureDesc(string Name) : VBPropertyLetProcedureDesc(Name)
{
    private static readonly Lazy<VBDeferredPropertyLetProcedureDesc> _instance = new(() => new(nameof(VBType)), LazyThreadSafetyMode.PublicationOnly);
    public new static VBDeferredPropertyLetProcedureDesc TypeInfo => _instance.Value;
}
