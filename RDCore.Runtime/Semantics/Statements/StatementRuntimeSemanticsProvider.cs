using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// Executes one <see cref="StatementNode"/> against a real session/frame, dispatching by the node's own
/// C# type — the statement analogue of <see cref="RDCore.Runtime.Semantics.RuntimeExpressionEvaluator"/>.
/// </summary>
public interface IStatementRuntimeSemanticsProvider
{
    /// <summary>
    /// Executes <paramref name="statement"/>, producing the control-flow outcome the executor loop
    /// reacts to.
    /// </summary>
    RuntimeExecutionOutcome Execute(IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement);
}

/// <inheritdoc cref="IStatementRuntimeSemanticsProvider"/>
public sealed class StatementRuntimeSemanticsProvider : IStatementRuntimeSemanticsProvider
{
    private readonly RuntimeExpressionEvaluator _expressionEvaluator;
    private readonly BinaryLetAssignmentOperatorRuntimeSemantics _letAssignment;
    private readonly ISetCoercionRuntimeSemantics _setCoercion;

    public StatementRuntimeSemanticsProvider(RuntimeExpressionEvaluator expressionEvaluator, ILetCoercionRuntimeSemanticsProvider letCoercionProvider, ISetCoercionRuntimeSemantics setCoercion, IVerboseMessageBuilder formatterService)
    {
        _expressionEvaluator = expressionEvaluator;
        _letAssignment = new(letCoercionProvider, formatterService);
        _setCoercion = setCoercion;
    }

    /// <inheritdoc/>
    public RuntimeExecutionOutcome Execute(IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement)
        => statement switch
        {
            AssignmentStatementNode { Kind: AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet } assignment => ExecuteLetAssignment(session, context, assignment),
            AssignmentStatementNode { Kind: AssignmentKind.Set } assignment => ExecuteSetAssignment(session, context, assignment),
            CallStatementNode call => ExecuteCall(session, context, call),
            _ => RuntimeExecutionOutcome.InternalError,
        };

    // MS-VBAL §5.4.2.1: both the explicit Call Foo(...) form and the bare Foo(...)/Foo form evaluate
    // Callee (its own argument list, if any, already part of its tree - see CallStatementNode's own
    // doc) and discard whatever it returns; a Sub's own Void result discards just as cleanly as a real
    // one would. The bare, unparenthesized multi-argument form (Foo 1, 2, populating Arguments directly
    // instead) isn't wired yet - S9a's own scope is the parenthesized/no-argument shapes only.
    private RuntimeExecutionOutcome ExecuteCall(IRuntimeSession session, RuntimeEvaluationContext context, CallStatementNode call)
    {
        if (!call.Arguments.IsEmpty)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var result = _expressionEvaluator.Evaluate(session, call.Callee, context);
        return result.IsSuccess ? RuntimeExecutionOutcome.Next
            : result.IsInternalError ? RuntimeExecutionOutcome.InternalError
            : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
    }

    // MS-VBAL §5.4.3.8. Scoped to a target that already resolves to a plain Symbol, same as
    // BinaryLetAssignmentOperatorRuntimeSemantics itself documents - a member-access or indexed target
    // needs procedure-invocation machinery that doesn't exist yet.
    private RuntimeExecutionOutcome ExecuteLetAssignment(IRuntimeSession session, RuntimeEvaluationContext context, AssignmentStatementNode assignment)
    {
        if (assignment.Target is not SimpleNameExpressionNode simpleName)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var targetResult = session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope);
        if (targetResult.Symbol is not { } target)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var valueResult = _expressionEvaluator.Evaluate(session, assignment.Value, context);
        if (!valueResult.IsSuccess)
        {
            return valueResult.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(valueResult.ErrorInfo!);
        }

        // the reserved synthetic "__let_op" binary operator - the same shape its own test suite
        // exercises it with: a throwaway node carrying this statement's own identity/location, operands
        // passed directly rather than read back off the node's Children.
        var syntheticOperator = new VBBinaryOperatorExpressionNode(OperatorSymbolNames.BinaryAssignmentValueOp, assignment.Identity, assignment.SourceLocation,
            assignment.Target, assignment.Value);
        var result = _letAssignment.Evaluate(session, new(), syntheticOperator, new VBSymbolDescValue(target), valueResult.Result!);

        return result.IsSuccess ? RuntimeExecutionOutcome.Next
            : result.IsInternalError ? RuntimeExecutionOutcome.InternalError
            : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
    }

    // MS-VBAL §5.4.3.9. Same target scope limitation as Let: a member-access or indexed target needs
    // procedure-invocation machinery (a Property Set call) that doesn't exist yet.
    private RuntimeExecutionOutcome ExecuteSetAssignment(IRuntimeSession session, RuntimeEvaluationContext context, AssignmentStatementNode assignment)
    {
        if (assignment.Target is not SimpleNameExpressionNode simpleName)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var targetResult = session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope);
        if (targetResult.Symbol is not ITypedSymbol target)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var valueResult = _expressionEvaluator.Evaluate(session, assignment.Value, context);
        if (!valueResult.IsSuccess)
        {
            return valueResult.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(valueResult.ErrorInfo!);
        }

        // Set-coercion (MS-VBAL §5.5.2.2) is not an operator - the same direct entry point
        // WithStatementRuntimeSemantics already uses for its own With-target coercion.
        var coercionResult = _setCoercion.EvaluateSetCoercion(session, assignment.Value, valueResult.Result!, target.ResolvedType);
        if (!coercionResult.IsSuccess)
        {
            return RuntimeExecutionOutcome.Error(coercionResult.ErrorInfo!);
        }

        var handle = session.Symbols.Resolver.GetValue((Symbol)target);
        if (!handle.BindingCapabilities.HasFlag(BindingCapabilities.SetValue))
        {
            // MS-VBAL static semantics should already have rejected an assignment to a read-only
            // target (a Const, chiefly) at compile time - reaching here means that check was skipped.
            return RuntimeExecutionOutcome.InternalError;
        }

        handle.SetValue(session.Symbols.Resolver, coercionResult.Result!.RuntimeValue);
        return RuntimeExecutionOutcome.Next;
    }
}
