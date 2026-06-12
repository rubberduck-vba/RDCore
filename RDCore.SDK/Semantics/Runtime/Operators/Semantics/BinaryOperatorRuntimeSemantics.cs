using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract.Operators;
using RDCore.SDK.Semantics.Runtime.LetCoercion;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.Operators.Semantics
{
    /// <summary>
    /// Provides <c>virtual</c> overloads to simplify the implementation of <em>binary operators</em> runtime semantics.
    /// </summary>
    public abstract record class BinaryOperatorRuntimeSemantics<TContext, TFlags>(
        ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider, 
        IVerboseMessageBuilder FormatterService) 
        : OperatorRuntimeSemantics<TContext, TFlags>(LetCoercionSemanticsProvider, FormatterService)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
    {

        /// <summary>
        /// The operator has the declared type returned by this method, based on the declared type of its operands.
        /// </summary>
        /// <param name="resolver">The execution context and memory space to operate with.</param>
        /// <param name="context">The semantic context of the operation, returned by <c>Analyze</c> just before this evaluation.</param>
        /// <param name="frame">The evaluaation frame for this operation.</param>
        /// <remarks>
        /// 🧩 Each implementation layer is <c>sealed</c> and yields <see cref="DetermineOperatorEffectiveTypeResult.NotApplicable"/> to defer to a more specialized implementation;
        /// the semantics are <em>hierarchically composed</em> through inheritance.<br/>
        /// 💥 The evaluation pipeline handles throwing the <see cref="VBRuntimeErrorId.TypeMismatch"/> error when no <em>effective type</em> is ultimately determined.
        /// </remarks>
        /// <returns><see cref="DetermineOperatorEffectiveTypeResult.NotApplicable"/> if no type is statically valid for this operation.</returns>
        protected abstract DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
            ISymbolResolver resolver, 
            SemanticContext<TFlags> context,
            VBBinaryOperatorExpression<TContext, TFlags> expression,
            OperatorEvaluationFrame frame);

        public sealed override DetermineOperatorEffectiveTypeResult DetermineOperatorEffectiveType(
            ISymbolResolver resolver,
            SemanticContext<TFlags> context,
            VBOperatorExpression<TContext, TFlags> expression,
            OperatorEvaluationFrame frame) => DetermineBinaryOperatorEffectiveType(resolver, context, (VBBinaryOperatorExpression<TContext, TFlags>)expression, frame);

        protected sealed override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
            IVBExecutionContext runtime,
            SemanticContext<TFlags> context,
            VBOperatorExpression<TContext, TFlags> expression,
            OperatorEvaluationFrame frame) => EvaluateExpressionResult(runtime, context, (VBBinaryOperatorExpression<TContext, TFlags>)expression, frame);

        protected abstract RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
            IVBExecutionContext runtime,
            SemanticContext<TFlags> context,
            VBBinaryOperatorExpression<TContext, TFlags> expression,
            OperatorEvaluationFrame frame);

        /// <summary>
        /// Evaluates the <see cref="VBNullType"/> runtime semantics of a <em>binary operator expression</em>.<br/>
        /// 👉 <strong>MS-VBAL</strong> binary operations with a <see cref="VBNullType"/> <em>effective data type</em> always results in a <see cref="VBNullValue"/>.
        /// </summary>
        /// <param name="symbol">The <c>ResultSymbol</c> of the <em>binary arithmetic operator expression</em>.</param>
        /// <remarks>
        /// 🧩 This method is <c>virtual</c> and intended to be overridden by derived semantics as needed.<br/>
        /// The base implementation creates a <see cref="VBNullValue"/> associated with the <c>ResultSymbol</c> of the <em>binary operator expression</em>
        /// and returns it in a <c>Success</c> result.
        /// </remarks>
        /// <returns>
        /// A <see cref="RuntimeSemanticsEvaluationResult"/> describing the evaluation result. 
        /// <list type="bullet">
        /// <item><strong>If successful</strong>, the operation <c>Result</c> is a <see cref="VBTypedValue"/> of the <em>effective data type</em> of the operation.</item>
        /// <item>Otherwise, the result contains a <see cref="VBRuntimeErrorInfo"/> describing a specific run-time error.</item>
        /// </list>
        /// </returns>
        protected virtual RuntimeSemanticsEvaluationResult EvaluateNullBinaryExpressionResult(Symbol symbol) 
            => RuntimeSemanticsEvaluationResult.Success(VBTypedValueFactory.CreateNullValue(symbol));
    }
}
