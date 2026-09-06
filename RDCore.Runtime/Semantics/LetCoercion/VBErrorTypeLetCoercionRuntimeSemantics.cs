using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.9 Let-coercion to and from <c>VBErrorType</c>
/// </summary>
public record class VBErrorTypeLetCoercionRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService) 
    : LetCoercionRuntimeSemantics<VBErrorType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        LetCoercionStackFrame frame) => frame.SourceValue switch
        {
            VBErrorValue when frame.DestinationTypeDesc.GetTargetType() is not VBVariantType and not VBFixedSizeArrayType =>
                LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame), frame),

            VBNumericTypedValue or VBBooleanValue or VBDateValue or VBStringValue or VBArrayValue or VBUserDefinedTypeValue =>
                LetCoercionProvider.EvaluateLetCoercionSemantics(resolver, expression, new(
                    NodeId: expression.Identity,
                    OperandIndex: frame.OperandIndex,
                    SourceValue: frame.SourceValue,
                    DestinationTypeDesc: new VBTypeDescValue(VBDoubleType.TypeInfo)
                )).Result is VBDoubleValue coerced
                    && (double)coerced.RuntimeValue.BoxedValue > VBErrorType.MinimumStdErrorValue 
                    && (double)coerced.RuntimeValue.BoxedValue < VBErrorType.MaximumStdErrorValue
                        ? LetCoercionResult.Success(new VBErrorValue((int)(double)coerced.RuntimeValue.BoxedValue))
                        : LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame), frame),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        LetCoercionStackFrame frame) => builder.AddFlags(ConversionSemanticFlags.ErrorOperand);
}
