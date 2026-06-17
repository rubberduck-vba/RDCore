using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Meta;

/// <summary>
/// An abstract meta-type representing any <c>VBPropertyGetMemberSymbol</c>
/// </summary>
/// <param name="Name">The name of the <c>Property Get</c> member</param>
public record class VBPropertyGetProcedureDesc(string Name, ImmutableArray<VBParameterSymbol> Parameters) : VBFunctionProcedureDesc(Name, Parameters)
{
    private static readonly Lazy<VBPropertyGetProcedureDesc> _instance = new(() => new(nameof(VBType), []), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Describes a specific <c>Property Get</c> procedure member.
    /// </summary>
    public static new VBPropertyGetProcedureDesc TypeInfo => _instance.Value;

    // NOTE: a value of this type is VBUnknown until determined with name resolution semantics.
    private static readonly Lazy<VBTypedValue> _defaultValue = new(() => VBUnknownValue.DefaultValue, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}

/// <summary>
/// Describes a <em>deferred property (get) member</em>; an accepted but unresolved member of an existing or deferred module type.
/// </summary>
/// <remarks>
/// Encountering a <em>deferred member</em> dring semantic traversal attaches the required semantics to produce a <c>VBInferredTypeMember</c> that can be materialized into a code action.
/// </remarks>
/// <param name="Name">The name of the deferred <c>Property Get</c> member</param>
public sealed record class VBDeferredPropertyGetProcedureDesc(string Name, ImmutableArray<VBParameterSymbol> Parameters) : VBPropertyGetProcedureDesc(Name, Parameters)
{
    private static readonly Lazy<VBDeferredPropertyGetProcedureDesc> _instance = new(() => new(nameof(VBType), []), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Describes a specific <em>deferred</em> <c>Property Get</c> procedure member.
    /// </summary>
    public new static VBDeferredPropertyGetProcedureDesc TypeInfo => _instance.Value;
}
