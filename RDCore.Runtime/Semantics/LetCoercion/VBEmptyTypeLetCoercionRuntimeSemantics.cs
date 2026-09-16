using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.11 Let-coercion to and from <c>VBEmptyType</c>
/// </summary>
public record class VBEmptyTypeLetCoercionRuntimeSemantics(IVerboseMessageBuilder FormatterService) 
    : LetCoercionRuntimeSemantics<VBEmptyType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion(ISymbolResolver resolver, VBOperatorExpression expression, LetCoercionStackFrame frame) =>
        frame.DestinationTypeDesc.Target switch
        {
            // MS-VBAL 5.5.1.2.11: "The result is 0." — Empty carries no runtime value of its own
            // (VBEmptyValue isn't a VBNumericTypedValue), so the destination's zero is constructed
            // directly rather than reinterpreted from the source.
            VBNumericType numericType => LetCoercionResult.Success(numericType.CreateValue(0d)),
        
            VBBooleanType => LetCoercionResult.Success(VBBooleanValue.False),

            VBDateType => LetCoercionResult.Success(VBDateType.TypeInfo.CreateValue(new ValueBindingHandle(VBDateType.Zero.RuntimeValue))),
            VBFixedStringType fixedStringDestinationType => LetCoercionResult.Success(
                fixedStringDestinationType.DefaultValue),

            VBStringType => LetCoercionResult.Success(VBStringValue.ZeroLengthString),
        
            // 🧩 what would be the implications of let-coercing Empty to Nothing?
            VBObjectType or VBClassType => LetCoercionResult.Error(OnLetCoercionObjectRequired(expression, frame)),

            /** IMPLEMENTATION NOTE
             * 👉 the negatively-specified type mismatch should be already handled by the caller if we just return NotApplicable here.
             */
            not VBVariantType => LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame)),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        LetCoercionStackFrame frame)
    {
        if (expression is VBUnaryOperatorExpressionNode)
        {
            builder.AddLetCoercionFlags(ConversionSemanticFlags.UnaryOperand);
        }
        else
        {
            builder.AddLetCoercionFlags(frame.OperandIndex == InputIndex.BinaryLeftOperand 
                ? ConversionSemanticFlags.BinaryLeftOperand 
                : ConversionSemanticFlags.BinaryRightOperand);
        }

        return builder;
    }
}
