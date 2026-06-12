using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
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
    /// MS-VBAL 5.5.1.2.12 Let-coercion to <c>VBVariantType</c>
    /// </summary>
    public record class VBVariantTypeLetCoercionRuntimeSemantics(
        ILetCoercionRuntimeSemanticsProvider Provider,
        IVerboseMessageBuilder FormatterService) 
        : LetCoercionRuntimeSemantics<VBVariantType>(FormatterService) 
    {
        public override LetCoercionResult EvaluateLetCoercion<TContext, TFlags>(
            ISymbolResolver resolver, 
            VBOperatorExpression<TContext, TFlags> expression, 
            LetCoercionStackFrame frame) => frame.SourceValue switch
            {
                not VBObjectValue and not VBNothingValue => 
                    LetCoercionResult.Success(VBTypedValueFactory.CreateVariant(frame.SourceValue, expression.ResultSymbol)),

                _ => LetCoercionResult.NotApplicable(frame)
            };

        protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation<TContext, TFlags>(
            ILetCoercionSemanticContextBuilder builder,
            ISymbolResolver resolver,
            VBOperatorExpression<TContext, TFlags> expression,
            LetCoercionStackFrame frame) => builder.AddFlags(ConversionSemanticFlags.VariantTarget);
    }
}
