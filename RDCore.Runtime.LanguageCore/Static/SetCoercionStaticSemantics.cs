using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.SDK.Semantics;
using RDCore.SDK;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.5.2.1
public record class SetCoercionStaticSemantics : StaticSemantics
{
    private static readonly Lazy<SetCoercionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static IStaticSemantics Instance => _instance.Value;

    public override StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        var source = operandDeclaredTypes[(int)InputIndex.CoercionSourceValue];
        var destinationType = operandDeclaredTypes[(int)InputIndex.CoercionDestinationType];
        if (!IsSetCoercionInvalid(source, destinationType))
        {
            return StaticSemanticsEvaluationResult.Success(destinationType);
        }

        // a Variant holds whatever it is Set to, and what is Set is an object: a value that is none is refused for what it is not (MS-VBA: "Object
        // required"); a variable of a class, or Object, refuses the value for its type (MS-VBA: "Type mismatch"). Both Excel-verified.
        var id = destinationType is VBVariantType ? VBCompileErrorId.ObjectRequired : VBCompileErrorId.TypeMismatch;
        return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(id, expression.Location, Exceptions.VBCompileError_SetCoercionTypeMismatch_Verbose));
    }

    private static bool IsSetCoercionInvalid(VBType source, VBType destination)
        => source is not VBUnknownType && destination is not VBUnknownType
            && (destination is not (VBClassType or VBObjectType or VBVariantType)
                || source is not (VBClassType or VBObjectType or VBVariantType));
}
