using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.1 Let-coercion between numeric types
/// </summary>
public sealed record class VBNumericLetCoercionTypeRuntimeSemantics(
    IVerboseMessageBuilder FormatterService, 
    ILetCoercionRuntimeSemanticsProvider Provider) 
    : LetCoercionRuntimeSemantics<VBNumericType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion<TContext, TFlags>(
        ISymbolResolver resolver, VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame) => frame.SourceValue.TypeInfo switch
        {
            IIntegralNumericType when frame.DestinationTypeDesc.Target is INumericType
                // if the source value is within the range of the destination type, the result is a copy of the value.
                => ValidateDestinationTypeRange(expression, frame, out var numericCoercionError)
                    ? LetCoercionResult.Success(
                        VBTypedValueFactory.CreateValue(frame.DestinationTypeDesc, expression.ResultSymbol, (VBNumericTypedValue)frame.SourceValue))
                    : LetCoercionResult.Error(numericCoercionError),

            IFloatingPointNumericType or IFixedPointNumericType when frame.DestinationTypeDesc.Target is IIntegralNumericType
                // if the source value is finite (not NaN or +/- infinity) and within the range of the destination type,
                // the result is the value converted to an integer using Banker's Rounding.
                => ValidateDestinationTypeRange(expression, frame, out var integralCoercionError)
                    ? LetCoercionResult.Success(
                        // NOTE semantic flags should note a lossy conversion here;
                        // if the source value is small enough, it can convert to zero.
                        // IMPLEMENTATION NOTE: MS-VBAL actually makes the above remark about lossy conversion in the next block.
                        VBTypedValueFactory.CreateValue(frame.DestinationTypeDesc, expression.ResultSymbol, 
                            VBNumericType.BankersRounding((VBNumericTypedValue)frame.SourceValue)))
                    : LetCoercionResult.Error(integralCoercionError),

            IIntegralNumericType when frame.DestinationTypeDesc.Target is IFloatingPointNumericType or IFixedPointNumericType
                // IMPLEMENTATION NOTE: MS-VBAL defines this block using a copy of the previous block *and* notes a lossy conversion:
                // > if the source value is finite (not NaN or +/- infinity) and within the range of the destination type,
                // > the result is the value converted to an integer using Banker's Rounding.
                // This is clearly an error in the MS-VBAL document, because no integer value will ever meet these conditions,
                // and the conversion is clearly a widening one in this case.
                // This implementation skips this technically specified check, because including it would be mathematically wrong,
                // and would also needlessly complicate the null-handling of floatCoercionError.
                //      && !double.IsNaN(sourceValue.ManagedValue) && !double.IsInfinity(sourceValue.ManagedValue) 
                => ValidateDestinationTypeRange(expression, frame, out var floatCoercionError)
                    ? LetCoercionResult.Success(
                        VBTypedValueFactory.CreateValue(frame.DestinationTypeDesc, expression.ResultSymbol, (VBNumericTypedValue)frame.SourceValue))
                    : LetCoercionResult.Error(floatCoercionError),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation<TContext, TFlags>(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        VBOperatorExpression<TContext, TFlags> expression,
        LetCoercionStackFrame frame) => 
        builder.AddFlags(ConversionSemanticFlags.LetCoerced
            // IMPLEMENTATION NOTE: LetCoercionRuntimeSemantics does not know about its own context.
            // Following MS-VBAL we should be adding an 'Implicit' flag here, but the introdction of an
            // explicit [__c()_op] coercion operator changes this: statement-level analysis must set the Implicit|Explicit coercion flags.
            // | ConversionSemanticFlags.Implicit
            | ConversionSemanticFlags.Numeric 
            | ConversionSemanticFlags.CTypeAvailable 
            | frame.SourceValue.TypeInfo switch
            {
                IFloatingPointNumericType or IFixedPointNumericType when frame.DestinationTypeDesc.Target is IIntegralNumericType
                    => ConversionSemanticFlags.Narrowing 
                     | ConversionSemanticFlags.Lossy | ConversionSemanticFlags.BankersRounding,

                IIntegralNumericType when frame.DestinationTypeDesc.Target is IFloatingPointNumericType or IFixedPointNumericType
                    => ConversionSemanticFlags.Widening,

                _ => 0
            });
}
