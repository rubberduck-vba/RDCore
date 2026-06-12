using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types
{
    /// <summary>
    /// A semantic data type representing an unknown (unresolved) but presumably valid data type.
    /// </summary>
    public sealed record class VBUnknownType() : VBType(typeof(object), VBTypeNames.VBUnknown)
    {
        private static readonly Lazy<VBUnknownType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// The <c>Unknown</c> (unresolved) data type.
        /// </summary>
        public static VBType TypeInfo => _instance.Value;

        private readonly Lazy<VBTypedValue> _defaultValue = new(() => VBEmptyValue.Empty, LazyThreadSafetyMode.PublicationOnly);
        public override VBTypedValue DefaultValue => _defaultValue.Value;

        public override int Size => sizeof(int);
    }
}
