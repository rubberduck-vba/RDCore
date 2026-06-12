#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types
{
    /// <summary>
    /// A <see cref="VBIntrinsicType{string?}"/> representing the <c>String</c> data type.
    /// </summary>
    /// <remarks>
    /// The <em>managed type</em> of a value of this data type is a <strong>nullable</strong> <c>string</c>.<br/>
    /// </remarks>
    public record class VBStringType() : VBIntrinsicType<string?>(VBTypeNames.VBString)
    {
        private static readonly Lazy<VBStringType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// The <c>String</c> data type.
        /// </summary>
        public static VBStringType TypeInfo => _instance.Value;

        private static readonly Lazy<VBStringValue> _defaultValue = new(() => VBStringValue.VBNullString, LazyThreadSafetyMode.PublicationOnly);
        public override VBTypedValue DefaultValue => _defaultValue.Value;

        /// <summary>
        /// Gets the size of a string pointer.
        /// </summary>
        public override int Size => sizeof(int);
    }
}
