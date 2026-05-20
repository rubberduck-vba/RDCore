using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// MS-VBAL 5.5.1.1 Let-coercion (static semantics)
/// </summary>
public record class LetCoercionStaticSemantics : StaticSemantics
{
    private static readonly Lazy<LetCoercionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static StaticSemantics Instance => _instance.Value;

    public override VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes)
    {
        var sourceType = operandDeclaredTypes[0];
        var destinationType = operandDeclaredTypes[1];
        return IsLetCoercionInvalid(sourceType, destinationType) ? default : destinationType;
    }

    private bool IsLetCoercionInvalid(VBType source, VBType destination)
    {
        return source switch
        {
            VBType 
                when destination is VBArrayType arrayType && 
                    arrayType.DeclaredValue is VBResizableArrayValue => true,
            
            INumericType or VBBooleanType or VBDateType 
                when destination is VBArrayType arrayType && 
                    arrayType.DeclaredValue is VBResizableArrayValue && 
                    arrayType.DeclaredValue.ItemType is VBByteType => true,

            // "any type except"...
            VBVariantType 
                when destination is VBArrayType arrayType && // "any non-byte resizable array"
                    arrayType.DeclaredValue is VBResizableArrayValue &&
                    arrayType.DeclaredValue.ItemType is not VBByteType => false, // <- "except"
            VBArrayType srcArrayType
                when srcArrayType.DeclaredValue is VBResizableArrayValue &&
                    srcArrayType.DeclaredValue.ItemType is not VBByteType &&
                    destination is VBArrayType arrayType && // "any non-byte resizable array"
                    arrayType.DeclaredValue is VBResizableArrayValue &&
                    arrayType.DeclaredValue.ItemType is not VBByteType => false, // <- "except"
            VBArrayType srcArrayType
                when srcArrayType.DeclaredValue is VBFixedSizeArrayValue &&
                    destination is VBArrayType arrayType && // "any non-byte resizable array"
                    arrayType.DeclaredValue is VBResizableArrayValue &&
                    arrayType.DeclaredValue.ItemType is not VBByteType => false, // <- "except"

            not VBVariantType and not VBUserDefinedType when destination is VBUserDefinedType => true,

            not VBVariantType when destination is VBClassType or VBObjectType => true,



            _ => false
        };
    }
}