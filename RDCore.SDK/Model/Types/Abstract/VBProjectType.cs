using RDCore.SDK.Model.Values.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Abstract
{
    /// <summary>
    /// The base data type for a project or library - the object at the top of any symbol hierarchy, that defines the root workspace <c>Uri</c> for everything under it..
    /// </summary>
    /// <param name="Name">The semantically valid identifier name of this project or library.</param>
    /// <param name="Uri">The root workspace <c>Uri</c> for this project or library.</param>
    /// <param name="ProjectTypeId">Describes the type of project or library.</strong></param>
    public abstract record class VBProjectType(string Name, Uri Uri, ProjectTypeId ProjectTypeId) : VBType(typeof(object), Name)
    {
        private static readonly Lazy<VBTypedValue> _defaultValue = new(() => VBLongPtrType_x86.TypeInfo.DefaultValue, LazyThreadSafetyMode.PublicationOnly);
        public override VBTypedValue DefaultValue => _defaultValue.Value;
        public override int Size => sizeof(int);

        /// <summary>
        /// An immutable array containing a <c>Uri</c> identifying each module in this library.
        /// </summary>
        public ImmutableArray<Uri> Modules { get; init; } = [];
        public VBProjectType WithModules(IEnumerable<Uri> modules) => this with { Modules = [.. modules] };
    }
}
