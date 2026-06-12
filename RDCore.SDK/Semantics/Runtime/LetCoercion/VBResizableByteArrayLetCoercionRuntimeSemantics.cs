using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.LetCoercion
{
    /// <summary>
    /// MS-VBAL 5.5.1.2.6 Let-coercion to and from <c>VBResizableByteArray</c>
    /// </summary>
    public record class VBResizableByteArrayLetCoercionRuntimeSemantics(
        ILetCoercionRuntimeSemanticsProvider Provider,
        IVerboseMessageBuilder FormatterService)
        : LetCoercionRuntimeSemantics<VBResizableByteArrayType>(FormatterService)
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
            LetCoercionStackFrame frame)
        {
            throw new NotImplementedException();
        }
    }

}
