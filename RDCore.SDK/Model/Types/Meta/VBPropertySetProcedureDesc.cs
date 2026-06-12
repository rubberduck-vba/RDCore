using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Meta
{
    /// <summary>
    /// An abstract meta-type representing any <c>VBPropertySetMemberSymbol</c>
    /// </summary>
    /// <param name="Name">The name of the <c>Property Set</c> member</param>
    public record class VBPropertySetProcedureDesc(string Name, ImmutableArray<VBParameterSymbol> Parameters) : VBProcedureMemberDesc(Name, Parameters)
    {
        private static readonly Lazy<VBPropertySetProcedureDesc> _instance = new(() => new(nameof(VBType), []), LazyThreadSafetyMode.PublicationOnly);
        public static new VBPropertySetProcedureDesc TypeInfo => _instance.Value;

        private static readonly Lazy<VBTypedValue> _defaultValue = new(() => VBUnknownValue.DefaultValue, LazyThreadSafetyMode.PublicationOnly);
        public override VBTypedValue DefaultValue => _defaultValue.Value;
    }

    /// <summary>
    /// Describes a <em>deferred property (Set) member</em>; an accepted but unresolved member of an existing or deferred module type.
    /// </summary>
    /// <remarks>
    /// Encountering a <em>deferred member</em> dring semantic traversal attaches the required semantics to produce a <c>VBInferredTypeMember</c> that can be materialized into a code action.
    /// </remarks>
    public sealed record class VBDeferredPropertySetProcedureDesc(string Name, ImmutableArray<VBParameterSymbol> Parameters) : VBPropertySetProcedureDesc(Name, Parameters)
    {
        private static readonly Lazy<VBDeferredPropertySetProcedureDesc> _instance = new(() => new(nameof(VBType), []), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Describes a specific <em>deferred</em> <c>Property Set</c> procedure member.
        /// </summary>
        public new static VBDeferredPropertySetProcedureDesc TypeInfo => _instance.Value;
    }
}
