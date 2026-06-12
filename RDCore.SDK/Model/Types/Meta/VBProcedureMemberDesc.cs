using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Meta
{
    /// <summary>
    /// An abstract meta-type representing any <c>VBProcedureMemberSymbol</c>
    /// </summary>
    /// <param name="Name">The name of the member</param>
    public record class VBProcedureMemberDesc(string Name, ImmutableArray<VBParameterSymbol> Parameters) : VBMemberDesc(Name, Parameters)
    {
        private static readonly Lazy<VBProcedureMemberDesc> _instance = new(() => new(nameof(VBType), []), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Describes a specific <c>Sub</c> procedure member.
        /// </summary>
        public static VBProcedureMemberDesc TypeInfo => _instance.Value;

        private static readonly Lazy<VBTypedValue> _defaultValue = new(() => VBUnknownValue.DefaultValue, LazyThreadSafetyMode.PublicationOnly);
        public override VBTypedValue DefaultValue => _defaultValue.Value;
    }

    /// <summary>
    /// Describes a <em>deferred procedure member</em>; an accepted but unresolved member of an existing or deferred module type.
    /// </summary>
    /// <remarks>
    /// Encountering a <em>deferred member</em> dring semantic traversal attaches the required semantics to produce a <c>VBInferredTypeMember</c> that can be materialized into a code action.
    /// </remarks>
    /// <param name="Name">The name of the deferred <c>Sub</c> procedure member.</param>
    public record class VBDeferredProcedureMemberDesc(string Name, ImmutableArray<VBParameterSymbol> Parameters) : VBProcedureMemberDesc(Name, Parameters)
    {
        private static readonly Lazy<VBDeferredProcedureMemberDesc> _instance = new(() => new(nameof(VBType), []), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Describes a specific <em>deferred</em> <c>Sub</c> procedure member.
        /// </summary>
        public new static VBDeferredProcedureMemberDesc TypeInfo => _instance.Value;
    }
}