#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types
{
    /// <summary>
    /// A <see cref="VBIntrinsicType{object}"/> representing the <c>Variant</c> data type.
    /// </summary>
    /// <remarks>
    /// The <em>managed type</em> of a value of this data type is <c>object</c>.<br/>
    /// 👉 The <c>VBVariant</c> subtype can be any <see cref="VBType"/> including <c>VBVariant</c>, or otherwise semantically invalid data types such as <see cref="VBUnknownType"/>.
    /// </remarks>
    /// <param name="SubType">The <em>variant subtype</em>, or <em>wrapped</em> data type.</param>
    public sealed record class VBVariantType(VBType SubType) : VBIntrinsicType<object>(VBTypeNames.VBVariant)
    {
        private static readonly Lazy<VBVariantType> _instance = new(() => new(VBEmptyType.TypeInfo), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// The <c>Variant</c> data type.
        /// </summary>
        public static VBVariantType TypeInfo => _instance.Value;

        private static readonly Lazy<VBVariantValue> _defaultValue = new(() => new(VBEmptyValue.Empty, GlobalSymbols.StaticSymbols.Empty), LazyThreadSafetyMode.PublicationOnly);
        public override VBVariantValue DefaultValue => _defaultValue.Value;
        /// <summary>
        /// The <em>variant subtype</em>, or <em>wrapped</em> data type.
        /// </summary>
        public VBType Subtype => SubType;

        public override int Size => sizeof(int); // FIXME this is a lie
    }

}
