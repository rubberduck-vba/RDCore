using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.LetCoercion
{
    /// <summary>
    /// MS-VBAL 5.5.1.2.7 Let-coercion to and from <c>VBResizableArray</c>
    /// </summary>
    public record class VBResizableArrayLetCoercionRuntimeSemantics(
        ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
        IVerboseMessageBuilder FormatterService) 
        : LetCoercionRuntimeSemantics<VBResizableArrayType>(FormatterService)
    {
        public override LetCoercionResult EvaluateLetCoercion<TContext, TFlags>(
            ISymbolResolver resolver, 
            VBOperatorExpression<TContext, TFlags> expression, 
            LetCoercionStackFrame frame)
        {
            throw new NotImplementedException();
        }

        protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation<TContext, TFlags>(
            ILetCoercionSemanticContextBuilder builder,
            ISymbolResolver resolver,
            VBOperatorExpression<TContext, TFlags> expression,
            LetCoercionStackFrame frame) => builder.AddFlags(ConversionSemanticFlags.ArrayTarget);
    }

}
