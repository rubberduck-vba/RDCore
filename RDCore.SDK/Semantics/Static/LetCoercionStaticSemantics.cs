using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static
{
    /// <summary>
    /// MS-VBAL 5.5.1.1 Let-coercion (static semantics)
    /// </summary>
    public record class LetCoercionStaticSemantics : IStaticSemantics
    {
        private static readonly Lazy<LetCoercionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
        public static IStaticSemantics Instance => _instance.Value;

        public VBType? DetermineDeclaredType(IVBExecutionContext context, params VBType[] operandDeclaredTypes)
        {
            var sourceType = operandDeclaredTypes[0];
            var destinationType = operandDeclaredTypes[1];
            return IsLetCoercionInvalid(sourceType, destinationType) ? default : destinationType;
        }

        /// <summary>
        /// NOTE: This section of the MS-VBAL specification is notoriously evil. 
        /// The mind-bending double-negative logic underlying this implementation is fully intentional.
        /// Readability tweaks welcome, but pattern-matching and helper functions are the only way to keep one's sanity here.
        /// </summary>
        /// <returns><c>true</c> if a let-coercion operation is <strong>invalid</strong> between the specified source and destination data types.</returns>
        private static bool IsLetCoercionInvalid(VBType source, VBType destination)
        {
            return source switch
            {
                VBType when destination is VBFixedSizeArrayType => true,
                INumericType or VBBooleanType or VBDateType when destination is VBResizableByteArrayType => true,
                VBType when destination is not VBResizableByteArrayType => true,
                VBType when AnyTypeExceptNonByteResizableArrayOr(source, VBFixedSizeArrayType.TypeInfo, VBVariantType.TypeInfo) && IsNonByteResizableArray(destination) => true,
                VBType and not VBUserDefinedType and not VBVariantType when destination is VBUserDefinedType => true,
                VBType and not VBVariantType when destination is VBObjectType or VBClassType => true,
                VBClassType type when type.DefaultMember is null || IsLetCoercionInvalid(type.DefaultMember.ResolvedType, destination) => true,
                VBFixedSizeArrayType or VBResizableArrayType and not VBResizableByteArrayType
                    when destination is VBVariantType || destination is VBResizableArrayType dstArray && source is VBArrayType srcArray && dstArray.ItemType != srcArray.ItemType => true,
                VBUserDefinedType srcUdt when destination is not VBVariantType && destination is VBUserDefinedType dstUdt && !srcUdt.Equals(dstUdt) => true,
                VBUserDefinedType and not VBExternalUserDefinedType when destination is VBVariantType => true,
                VBArrayType srcArray when srcArray.ItemType is VBUserDefinedType and not VBExternalUserDefinedType && destination is VBVariantType => true,
                VBArrayType srcArray when srcArray.ItemType is VBFixedStringType && destination is VBVariantType => true,
                // NOTE: this line is specified outside the section 5.5.1.1 table
                VBLongLongType when destination is not VBLongLongType and not VBVariantType => true,

                /*
                 * MS-VBAL 5.5.1.1 table specifies "source declared type OR LITERAL".
                 * NOT DOING THIS HERE: twisting StaticSemantics signature to work with VBTypedValue would make no sense.
                 * Alternatively we could treat literals as a first-class VBType or somehow semantically treat literals 
                 * as their declared type, having a shared base class for the two. It... twists the model in a weird way.
                */
                //VBType when source is VBNothingValue && destination is not VBObjectType and not VBVariantType => true,

                _ => false
            };
        }

        /// <summary>
        /// Helper function to twist the rules exactly in the same manner they're specified.
        /// </summary>
        /// <param name="source">The let-coercion <em>source</em> data type.</param>
        /// <param name="exceptTypes">The types to exclude from the rule match.</param>
        /// <returns><c>true</c> if the <c>source</c> type does not contain any of the <c>exceptTypes</c>.</returns>
        private static bool AnyTypeExceptNonByteResizableArrayOr(VBType source, params VBType[] exceptTypes) => !IsNonByteResizableArray(source) && !exceptTypes.Contains(source);
        /// <summary>
        /// Helper function to twist the rules exactly in the same manner they're specified.
        /// </summary>
        /// <param name="type">The data type to control.</param>
        /// <returns><c>true</c> if the <c>type</c> is a <c>VBResizableArrayType</c> but not a <c>VBResizableByteArrayType</c>.</returns>
        private static bool IsNonByteResizableArray(VBType type) => type is VBResizableArrayType and not VBResizableByteArrayType;
    }
}