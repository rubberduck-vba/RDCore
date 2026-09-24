using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context.Abstract;

namespace RDCore.SDK.Runtime.Abstract;

/// <summary>
/// Formalizes the interface of let-coercion runtime semantics.
/// </summary>
public interface ILetCoercionRuntimeSemantics
{
    Type LetCoercionSpecification { get; }

    /// <summary>
    /// Evaluates the let-coerced <c>VBTypedValue</c> for the specified <c>effectiveType</c> in the context of the specified <c>expression</c>.
    /// </summary>
    /// <param name="resolver">A service that can resolve symbols and their values in the current context.</param>
    /// <param name="expression">
    /// The expression whose evaluated value is being let-coerced. Usually a <c>VBOperatorExpression</c>
    /// (an operand of an operator that itself needs let-coercion semantics), but not always: a construct
    /// that forces a value to a specific type without an operator node of its own in source — a
    /// condition's truth test, for instance — coerces the same way and passes its own expression here.
    /// </param>
    /// <param name="frame">The current stack frame of the coercion operation.</param>
    /// <returns>An object that encapsulates the result of the operation, including any run-time errors to be thrown.</returns>
    LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame);

    /// <summary>
    /// Builds the semantic context of a let-coercion operation involving the semantics of a specific <c>VBType</c>.
    /// </summary>
    /// <param name="builder">Builds the semantic context of the conversion operation.</param>
    /// <param name="resolver">A symbol lookup service.</param>
    /// <param name="expression">The expression whose evaluated value is being let-coerced (see <see cref="EvaluateLetCoercion"/>).</param>
    /// <param name="frame">The current stack frame of the coercion operation.</param>
    /// <param name="result">The result of the let-coercion operation for the current stack frame.</param>
    /// <remarks>
    /// 🧩 <em>Analyzers</em> (<c>RDCore.Diagnostics</c> and other <em>plug-ins</em>) perform the actual analysis of the semantic context.
    /// </remarks>
    /// <returns>An <em>analysis context</em> for this let-coercion operation.</returns>
    LetCoercionAnalysisContext Analyze(
        ILetCoercionSemanticContextBuilder builder, 
        ISymbolResolver resolver, 
        ExpressionNode expression, 
        LetCoercionStackFrame frame,
        LetCoercionResult result);
}
