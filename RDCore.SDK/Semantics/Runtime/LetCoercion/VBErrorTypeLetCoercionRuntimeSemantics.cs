using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.LetCoercion
{
    /// <summary>
    /// MS-VBAL 5.5.1.2.9 Let-coercion to and from <c>VBErrorType</c>
    /// </summary>
    public record class VBErrorTypeLetCoercionRuntimeSemantics(
        ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
        IVerboseMessageBuilder FormatterService) 
        : LetCoercionRuntimeSemantics<VBErrorType>(FormatterService)
    {
        public override LetCoercionResult EvaluateLetCoercion<TContext, TFlags>(
            ISymbolResolver resolver,
            VBOperatorExpression<TContext, TFlags> expression,
            LetCoercionStackFrame frame) => frame.SourceValue switch
            {
                VBErrorValue when frame.DestinationTypeDesc.GetTargetType() is not VBVariantType and not VBFixedSizeArrayType =>
                    LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame), frame),

                VBNumericTypedValue or VBBooleanValue or VBDateValue or VBStringValue or VBArrayValue or VBUserDefinedTypeValue =>
                    LetCoercionProvider.EvaluateLetCoercionSemantics(resolver, expression, new(
                        NodeUri: expression.SemanticId,
                        OperatorSymbol: expression.Symbol,
                        OperandIndex: frame.OperandIndex,
                        SourceValue: frame.SourceValue,
                        DestinationTypeDesc: VBTypedValueFactory.DescribeType(VBDoubleType.TypeInfo, expression.ResultSymbol)
                    )).Result is VBDoubleValue coerced
                        && coerced.ManagedValue > VBErrorType.MinimumStdErrorValue 
                        && coerced.ManagedValue < VBErrorType.MaximumStdErrorValue
                            ? LetCoercionResult.Success(VBTypedValueFactory.CreateValue(VBErrorType.TypeInfo, expression.ResultSymbol, coerced.ManagedValue))
                            : LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame), frame),

                _ => LetCoercionResult.NotApplicable(frame)
            };

        protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation<TContext, TFlags>(
            ILetCoercionSemanticContextBuilder builder,
            ISymbolResolver resolver,
            VBOperatorExpression<TContext, TFlags> expression,
            LetCoercionStackFrame frame) => builder.AddFlags(ConversionSemanticFlags.ErrorOperand);
    }

}
