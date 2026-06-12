using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Complex
{
    /// <summary>
    /// Describes a data type that is undeclared but that can be statically inferred from its use.
    /// </summary>
    public interface IVBInferableType
    {
        /*** RD-VBA Inferable Types
         * 
         * The RDCore implementation of MS-VBAL formalizes the concept of an inferable VBType,
         * whith semantics that should be applicable to VBVariantType, as well as any deferrable types.
         * 
         * Static semantics
         *  - OPTION STRICT being specified in the declarations section of a module disables these semantics for that module.
         *  - Type inference semantics intervene:
         *    - When the semantic pass finds a SimpleNameExpression for which the resolved data type is VBUnknownType (unresolved)
         *    - When the semantic pass finds a MemberAccessExpression for which the resolved data type is VBUnknownType (unresolved):
         *    - When the semantic pass finds a DictionaryAccessExpression for which the resolved data type is VBUnknownType (unresolved):
         *  - When the unresolved expression is found in the RHS of any AssignmentExpression, the data type of the LHS operand is a candiate type.
         *  - When the unresolved expression is found in the LHS of any AssignmentExpression, the data type of the RHS operand is a candiate type.
         *  - When a new possible candidate type is found:
         *    - If the type is an integer numeric data type, the candidate is VBLongType.
         *    - If the type is a floating-point numeric data type, the candidate is VBDoubleType.
         *    - If the type is a fixed-point numeric data type, the candidate is VBCurrencyType.
         *    - If the type is a deferrable type, the candidate is merged with that deferrable type.
         *    - Conflicting candidate types may coexist under the same entity, unless one of them is a VBObjectType or VBClassType.
         *    - The resolved type of that node after this semantic pass depends on the types of the candidates:
         *      - If there is only one possible candidate data type, the resolved type is that candidate type.
         *      - Otherwise if all candidate data types are intrinsic types, the resolved type is VBVariantType.
         *  
         *  TODO continue this...
        */

        /// <summary>
        /// A set of determined legal data types for materializing this type.
        /// </summary>
        ImmutableHashSet<VBType> CandidateTypes { get; init; }
        /// <summary>
        /// Gets a copy of this inferable data type with the added specified <em>candidate</em> <c>VBType</c>.
        /// </summary>
        /// <param name="vbType">The candidate data type to be added.</param>
        IVBInferableType WithCandidateType(VBType vbType);
    }

    /// <summary>
    /// Describes a <em>complex data type</em> that is undeclared but that can be statically inferred and constructed from its use.
    /// </summary>
    public interface IVBDeferrableType : IVBInferableType
    {
        /*** RD-VBA Deferrable Types
         * 
         * The RDCore implementation of MS-VBAL formalizes the concept of a deferrable VBType,
         * which is an undeclared or otherwise unresolved data type that is also in active symbolic use.
         * 
         * This results in a program that is statically illegal in MS-VBA with OPTION EXPLICIT, 
         * and systematically fails the late binding runtime semantics without OPTION EXPLICIT.
         * 
         * Static semantics
         *  - OPTION STRICT being specified in the declarations section of a module disables these semantics for that module.
         *  - 
         *  
         *  TODO continue this...
        */

        /// <summary>
        /// Gets the <em>deferred type</em> that can be materialized from this <em>infered type</em>.
        /// </summary>
        IVBMemberOwnerType? DeferredVBType { get; init; }
        /// <summary>
        /// Gets a copy of this deferrable data type with the specified target <em>deferred type</em>.
        /// </summary>
        /// <param name="vbType">Any <c>VBType</c> that can have members.</param>
        /// <returns></returns>
        VBDeferredType WithDeferredVBType(IVBMemberOwnerType vbType);
    }

    public interface IVBDeferrableTypeMember : IVBInferableType
    {
        VBType? DeferredVBType { get; init; }
        VBDeferredTypeMemberSymbol WithDeferredVBType(VBType vbType);
    }

    public abstract record class VBDeferredType : VBType, IVBDeferrableType
    {
        public VBDeferredType(string name, Uri uri)
            : base(typeof(object), name, isHidden: true)
        {
            Uri = uri;
        }

        public override int Size => sizeof(int);

        public Uri Uri { get; init; }

        public ImmutableHashSet<VBDeferredTypeMemberSymbol> Members { get; init; } = [];
        public VBDeferredType WithMembers(IEnumerable<VBDeferredTypeMemberSymbol> members) => this with { Members = [.. members] };

        public IVBMemberOwnerType? DeferredVBType { get; init; }
        public VBDeferredType WithDeferredVBType(IVBMemberOwnerType vbType) => this with { DeferredVBType = vbType };

        public ImmutableHashSet<VBType> CandidateTypes { get; init; } = [];
        public IVBInferableType WithCandidateType(VBType vbType) => this with { CandidateTypes = [.. CandidateTypes, vbType] };
    }

}
