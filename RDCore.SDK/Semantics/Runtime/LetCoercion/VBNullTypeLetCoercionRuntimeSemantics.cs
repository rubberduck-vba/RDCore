using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.LetCoercion
{
    /// <summary>
    /// MS-VBAL 5.5.1.2.10 Let-coercion to and from <c>VBNullType</c>
    /// </summary>
    public record class VBNullTypeLetCoercionRuntimeSemantics(
        IVerboseMessageBuilder FormatterService)
        : LetCoercionRuntimeSemantics<VBNullType>(FormatterService)
    {
        public override LetCoercionResult EvaluateLetCoercion<TContext, TFlags>(
            ISymbolResolver resolver, 
            VBOperatorExpression<TContext, TFlags> expression, 
            LetCoercionStackFrame frame) => frame.SourceValue switch
            {
                VBNullValue when frame.DestinationTypeDesc.GetTargetType() is VBUserDefinedType or VBResizableArrayType 
                    => LetCoercionResult.Error(OnLetCoercionOverflow(expression, frame)),

                VBNullValue when frame.DestinationTypeDesc.GetTargetType() is not VBNullType and not VBFixedSizeArrayType and not VBVariantType 
                    => LetCoercionResult.Error(OnLetCoercionInvalidUseOfNull(expression, frame)),

                _ => LetCoercionResult.NotApplicable(frame)
            };

        protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation<TContext, TFlags>(
            ILetCoercionSemanticContextBuilder builder,
            ISymbolResolver resolver,
            VBOperatorExpression<TContext, TFlags> expression,
            LetCoercionStackFrame frame) => builder.AddFlags(ConversionSemanticFlags.NullOperand | 
                frame.SourceValue switch
                {
                    VBNullValue when frame.DestinationTypeDesc.GetTargetType() is VBUserDefinedType or VBResizableArrayType
                        => ConversionSemanticFlags.Failed,

                    VBNullValue when frame.DestinationTypeDesc.GetTargetType() is not VBNullType and not VBFixedSizeArrayType and not VBVariantType
                        => ConversionSemanticFlags.Failed,
                    _ => 0
                });
    }

}
