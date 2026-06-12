using RDCore.SDK.Extensibility;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Meta
{
    /// <summary>
    /// Describes a <c>Function</c> procedure.
    /// </summary>
    /// <param name="Name">The name of the member.</param>
    public record class VBFunctionProcedureDesc(string Name, ImmutableArray<VBParameterSymbol> Parameters) : VBMemberDesc(Name, Parameters)
    {
        private static readonly Lazy<VBFunctionProcedureDesc> _instance = new(() => new(nameof(VBType), []), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Describes a specific <c>Function</c> procedure.
        /// </summary>
        public static VBFunctionProcedureDesc TypeInfo => _instance.Value;

        // NOTE: a value of this type is VBUnknown until determined with name resolution semantics.
        private static readonly Lazy<VBTypedValue> _defaultValue = new(() => VBUnknownValue.DefaultValue, LazyThreadSafetyMode.PublicationOnly);
        public override VBTypedValue DefaultValue => _defaultValue.Value;
    }

    /// <summary>
    /// Describes a <em>deferred function member</em>; an accepted but unresolved member of an existing or deferred module type.
    /// </summary>
    /// <remarks>
    /// Encountering a <em>deferred member</em> dring semantic traversal attaches the required semantics to produce a <c>VBInferredTypeMember</c> that can be materialized into a code action.
    /// </remarks>
    /// <param name="Name">The name of the deferred member</param>
    public record class VBDeferredFunctionProcedureDesc(string Name, ImmutableArray<VBParameterSymbol> Parameters) : VBFunctionProcedureDesc(Name, Parameters)
    {
        private static readonly Lazy<VBDeferredFunctionProcedureDesc> _instance = new(() => new(nameof(VBType), []), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Describes a specific <em>deferred</em> <c>Function</c> procedure.
        /// </summary>
        public new static VBDeferredFunctionProcedureDesc TypeInfo => _instance.Value;
    }
}