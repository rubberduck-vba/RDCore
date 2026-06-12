using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Complex
{
    /// <summary>
    /// Represents a <em>standard module</em> type.
    /// </summary>
    /// <remarks>
    /// Standard module public variables are statically allocated in global scope.
    /// </remarks>
    /// <param name="Name">the <em>identifier name</em> of the module.</param>
    /// <param name="IsHidden"><c>true</c> if the module type is hidden.</param>
    public record class VBStdModuleType(string Name, bool IsHidden = false) : VBType(typeof(object), Name, IsHidden), IVBMemberOwnerType
    {
        /// <summary>
        /// Creates a new <em>standard module</em> type with the specified members.
        /// </summary>
        /// <param name="name">The <em>identifier name</em> of the module.</param>
        /// <param name="members">The member definition symbols under this module type.</param>
        /// <param name="isHidden"><c>true</c> if the module type is hidden.</param>
        public VBStdModuleType(string name, IEnumerable<VBTypeMemberSymbol>? members = null, bool isHidden = false)
            : this(name, isHidden)
        {
            Members = [.. members ?? []];
        }

        private static readonly Lazy<VBVoidValue> _defaultValue = new(() => VBVoidValue.Void, LazyThreadSafetyMode.PublicationOnly);
        public override VBTypedValue DefaultValue => _defaultValue.Value;

        public ImmutableArray<VBTypeMemberSymbol> Members { get; init; }

        public override int Size => 0; // TODO compute the size of the address space of the module?

        public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members) => this with { Members = [.. members] };
    }

    public record class VBDeferredModuleType(string Name, Uri Uri) : VBDeferredType(Name, Uri)
    {
        private static readonly Lazy<VBVoidValue> _defaultValue = new(() => VBVoidValue.Void, LazyThreadSafetyMode.PublicationOnly);
        public override VBTypedValue DefaultValue => _defaultValue.Value;
    }
}
