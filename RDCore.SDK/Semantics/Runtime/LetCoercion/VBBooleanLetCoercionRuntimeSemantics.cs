using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.LetCoercion
{
    /// <summary>
    /// MS-VBAL 5.5.1.2.2 Let-coercion to and from <c>VBBooleanType</c>
    /// </summary>
    public sealed record class VBBooleanLetCoercionRuntimeSemantics(
        ILetCoercionRuntimeSemanticsProvider Provider, 
        IVerboseMessageBuilder FormatterService) 
        : LetCoercionRuntimeSemantics<VBBooleanType>(FormatterService)
    {
        public sealed override LetCoercionResult EvaluateLetCoercion<TContext, TFlags>(ISymbolResolver resolver, VBOperatorExpression<TContext, TFlags> expression, LetCoercionStackFrame frame) => 
            frame.SourceValue switch
            {
                IFixedPointNumericType or IFloatingPointNumericType when frame.DestinationTypeDesc.Target is IIntegralNumericType and VBNumericType numericDestinationType
                    => ValidateDestinationTypeRange(expression, frame, out var error) 
                        ? LetCoercionResult.Success(VBTypedValueFactory.CreateValue(numericDestinationType, expression.ResultSymbol, ((VBNumericTypedValue)frame.SourceValue).ManagedValue))
                        : LetCoercionResult.Error(error),

                IIntegralNumericType when frame.DestinationTypeDesc.Target is IFixedPointNumericType or IFloatingPointNumericType
                    => ValidateDestinationTypeRange(expression, frame, out var error)
                        ? LetCoercionResult.Success(VBTypedValueFactory.CreateValue(frame.DestinationTypeDesc.Target, expression.ResultSymbol, ((VBNumericTypedValue)frame.SourceValue).ManagedValue))
                        : LetCoercionResult.Error(error),

                // NOTE: MS-VBAL specifies this block first, but the pattern-matching would make the other blocks unreacheable.
                VBNumericTypedValue numericSourceValue when frame.DestinationTypeDesc.Target is VBNumericType numericDestinationType
                    => ValidateDestinationTypeRange(expression, frame, out var error)
                        ? LetCoercionResult.Success(VBTypedValueFactory.CreateValue(numericDestinationType, expression.ResultSymbol, numericSourceValue.ManagedValue))
                        : LetCoercionResult.Error(error),

                _ => LetCoercionResult.NotApplicable(frame)
            };

        protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation<TContext, TFlags>(ILetCoercionSemanticContextBuilder builder, ISymbolResolver resolver, VBOperatorExpression<TContext, TFlags> expression, LetCoercionStackFrame frame)
        {
            builder.AddLetCoercionFlags(ConversionSemanticFlags.Numeric | ConversionSemanticFlags.CTypeAvailable | frame.SourceValue switch
            {
                VBNumericTypedValue numericSourceValue when frame.DestinationTypeDesc.Target is VBNumericType numericDestinationType
                    && numericSourceValue.Size > numericDestinationType.Size
                    && ValidateDestinationTypeRange(expression, frame, out _)
                        => ConversionSemanticFlags.Narrowing,

                VBNumericTypedValue numericSourceValue when frame.DestinationTypeDesc.Target is VBNumericType numericDestinationType
                    && numericSourceValue.Size < numericDestinationType.Size
                    && ValidateDestinationTypeRange(expression, frame, out _)
                        => ConversionSemanticFlags.Widening,

                _ => 0 // nop
            }, frame.OperandIndex);
            return builder;
        }
    }
}
