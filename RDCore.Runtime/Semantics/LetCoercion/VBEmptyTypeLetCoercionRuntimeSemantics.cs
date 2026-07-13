using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
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
    public override LetCoercionResult EvaluateLetCoercion<TContext, TFlags>(ISymbolResolver resolver, VBOperatorExpression<TContext, TFlags> expression, LetCoercionStackFrame frame) =>
        frame.DestinationTypeDesc.Target switch
        {
            VBNumericType numericType => LetCoercionResult.Success(
                VBTypedValueFactory.CreateValue(numericType, expression.ResultSymbol, ((VBNumericTypedValue)frame.SourceValue).ManagedValue.InteropValue!.Value)),
        
            VBBooleanType => LetCoercionResult.Success(
                VBTypedValueFactory.CreateBooleanValue(expression.ResultSymbol, VBBooleanValue.False)),

            VBDateType => LetCoercionResult.Success(VBTypedValueFactory.CreateValue(expression.ResultSymbol, VBDateType.Zero)),
            VBFixedStringType fixedStringDestinationType => LetCoercionResult.Success(
                VBTypedValueFactory.CreateValue(fixedStringDestinationType, expression.ResultSymbol)!),

            VBStringType => LetCoercionResult.Success(
                VBTypedValueFactory.CreateStringValue(expression.ResultSymbol, VBStringValue.ZeroLengthString)),
        
            // 🧩 what would be the implications of let-coercing Empty to Nothing?
            VBObjectType or VBClassType => LetCoercionResult.Error(OnLetCoercionObjectRequired(expression, frame)),

            /** IMPLEMENTATION NOTE
             * 👉 the negatively-specified type mismatch should be already handled by the caller if we just return NotApplicable here.
             */
            not VBVariantType => LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame)),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation<TContext, TFlags>(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        VBOperatorExpression<TContext, TFlags> expression,
        LetCoercionStackFrame frame)
    {
        if (expression is VBUnaryOperatorExpression<TContext, TFlags>)
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
