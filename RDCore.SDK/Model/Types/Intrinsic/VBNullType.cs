#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types
{
    /// <summary>
    /// A <see cref="VBIntrinsicType{int}"/> representing the <c>Null</c> data type.
    /// </summary>
    /// <remarks>
    /// The <em>managed type</em> of a value of this data type is <c>int</c>.<br/>
    /// 👉 This data type has no declaration semantics and is only indirectly usable as a <c>Variant</c> subtype.
    /// </remarks>
    public sealed record class VBNullType() : VBIntrinsicType<int>(VBTypeNames.VBNull)
    {
        private static readonly Lazy<VBNullType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// The <c>Null</c> data type.
        /// </summary>
        public static VBNullType TypeInfo => _instance.Value;

        private static readonly Lazy<VBNullValue> _defaultValue = new(() => VBNullValue.Null, LazyThreadSafetyMode.PublicationOnly);
        public override VBNullValue DefaultValue => _defaultValue.Value;
        public override int Size => sizeof(int);
    }
}
