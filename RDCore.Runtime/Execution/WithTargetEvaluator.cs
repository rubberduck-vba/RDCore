using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Evaluates a <c>With</c> statement's target expression and produces the value that would be stored in
/// its anonymous block variable (<strong>MS-VBAL §5.4.2.21</strong>).
/// </summary>
/// <param name="expressionEvaluator">Evaluates the target expression.</param>
/// <param name="withStatement">The runtime semantics of the statement.</param>
/// <param name="observation">Told what the statement is (<see cref="WithStatementFact"/>), when it is evaluated for an analysis.</param>
public sealed class WithTargetEvaluator(RuntimeExpressionEvaluator expressionEvaluator, WithStatementRuntimeSemantics withStatement, AnalysisObservation? observation = null)
{
    /// <summary>
    /// Evaluates <paramref name="node"/>'s target expression and Set/Let-coerces it, per
    /// <see cref="WithStatementRuntimeSemantics"/>.
    /// </summary>
    public RuntimeSemanticsEvaluationResult Evaluate(IRuntimeSession session, WithStatementNode node, RuntimeEvaluationContext context)
    {
        var targetResult = expressionEvaluator.Evaluate(session, node.WithExpression, context);
        if (!targetResult.IsSuccess)
        {
            return targetResult;
        }

        var target = targetResult.Result!;

        // a record that is a variable is the block variable itself (MS-VBAL §5.4.2.21), and what the block assigns to its fields is the variable's: the
        // Let-coercion the statement applies to a record is a copy (DataValues), which only a value - a function's result - is given.
        var result = target is VBUserDefinedTypeValue && DenotesAVariable(session, context, node.WithExpression)
            ? RuntimeSemanticsEvaluationResult.Success(target)
            : withStatement.Evaluate(session, new(), node, target);
        if (observation is { IsSuspended: false })
        {
            Observe(observation, session, node, target, result);
        }

        return result;
    }

    /// <summary>
    /// Whether the target <paramref name="expression"/> of a <c>With</c> statement is classified as a variable, and so is the With block variable
    /// itself (<strong>MS-VBAL §5.4.2.21</strong>), rather than a value the block holds in an anonymous one.
    /// </summary>
    /// <remarks>
    /// A variable is a name of a variable, a field of the module or a parameter; that name qualified by its module (<c>Module1.Kept</c>); a field of a
    /// record (<c>r.Item</c>); a public variable of an object whose class is declared (<c>obj.Child</c>); an element of an array (<c>items(1)</c>).
    /// Anything else is taken to be a value - <c>New</c>, a call, a property, and a member of an object that is late bound, which MS-VBAL classifies
    /// as an unbound member: the block holds it until it is over, which is what keeps it alive while it is in use.
    /// <para>
    /// 👉 Classified by the declared types the static semantics give the expression, not by evaluating it again: the target is evaluated once, and a
    /// call in it is made once.
    /// </para>
    /// </remarks>
    public bool DenotesAVariable(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression)
        => expressionEvaluator.DenotesAVariable(session, context, expression);

    // what the statement is, as its semantics analyze it. That it fails is only said of a target that is known, and of an error that is the code's.
    private void Observe(AnalysisObservation into, IRuntimeSession session, WithStatementNode node, VBTypedValue target, RuntimeSemanticsEvaluationResult result)
    {
        var builder = new SemanticContextFlagsBuilder<WithStatementSemanticContext, WithStatementSemanticFlags>();
        withStatement.Analyze(session, new ConversionOperationSemanticContext(), builder, node, target);

        var flags = builder.Build().Flags;
        if (!target.IsIndeterminate && !result.IsSuccess && result.ErrorInfo?.ErrorId != (int)VBRuntimeErrorId.InternalError)
        {
            flags |= WithStatementSemanticFlags.Failed;
        }

        into.OnStatement(new WithStatementFact(node.Identity, node.SourceLocation, Tokens.With, flags, IsValueKnown: !target.IsIndeterminate));
    }
}
