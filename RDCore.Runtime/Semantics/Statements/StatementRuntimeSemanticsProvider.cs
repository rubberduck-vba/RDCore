using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
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
    private readonly LetAssignmentEvaluator _assignments;
    private readonly ISetCoercionRuntimeSemantics _setCoercion;
    private readonly PrintOutputEvaluator _printOutput;
    private readonly ConditionEvaluator _conditions;
    private readonly FileStatementRuntimeSemantics _files;
    private readonly FixedAssignmentRuntimeSemantics _fixedAssignment;
    private readonly ArrayStatementRuntimeSemantics _arrays;

    public StatementRuntimeSemanticsProvider(RuntimeExpressionEvaluator expressionEvaluator, LetAssignmentEvaluator assignments, ISetCoercionRuntimeSemantics setCoercion, PrintOutputEvaluator printOutput, ConditionEvaluator conditions, FileStatementRuntimeSemantics files, FixedAssignmentRuntimeSemantics fixedAssignment, ArrayStatementRuntimeSemantics arrays)
    {
        _expressionEvaluator = expressionEvaluator;
        _assignments = assignments;
        _setCoercion = setCoercion;
        _printOutput = printOutput;
        _conditions = conditions;
        _files = files;
        _fixedAssignment = fixedAssignment;
        _arrays = arrays;
    }

    /// <inheritdoc/>
    public RuntimeExecutionOutcome Execute(IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement)
        => statement switch
        {
            AssignmentStatementNode { Kind: AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet } assignment => ExecuteLetAssignment(session, context, assignment),
            AssignmentStatementNode { Kind: AssignmentKind.Set } assignment => ExecuteSetAssignment(session, context, assignment),
            // MS-VBAL §5.4.3.6-7: LSet and RSet fit a value into the target's own current width.
            AssignmentStatementNode { Kind: AssignmentKind.LSet or AssignmentKind.RSet } assignment => _fixedAssignment.Execute(session, context, assignment),
            // MS-VBAL §5.4.3.3-4: the statements that change an array's shape.
            RedimDeclarationNode redim => _arrays.ExecuteRedim(session, context, redim),
            KeywordStatementNode { Token: Tokens.Erase } erase => _arrays.ExecuteErase(session, context, erase),
            // Debug.Print: MS-VBAL §5.4.5.8's output rules, against the session's output rather than a
            // file. The parser gives these a node of their own, so this is a type test rather than a
            // match on the spelling of a call's owner - and a build that lowers them away never gets
            // here at all.
            DebugPrintStatementNode print => _printOutput.Execute(session, context, print.Items),
            DebugAssertStatementNode assert => ExecuteAssert(session, context, assert),
            // MS-VBAL §5.4.5.1/.2: the statements that associate and disassociate a file number.
            OpenStatementNode open => _files.ExecuteOpen(session, context, open),
            KeywordStatementNode { Token: Tokens.Close or Tokens.Reset } close => _files.ExecuteClose(session, context, close),
            // MS-VBAL §5.4.5.8: Print to a file channel. The channel exists now; writing to one does not yet,
            // and the bare object-relative form needs an enclosing form or report, which does not either.
            // MS-VBAL §5.4.5.8-9. The bare object-relative form needs an enclosing form or report, which does
            // not exist; ExecutePrint reports that itself.
            PrintStatementNode print => _files.ExecutePrint(session, context, print),
            // MS-VBAL §5.4.5.6: Line Input # reads one line and Let-assigns it.
            KeywordStatementNode { Token: Tokens.LineInput } lineInput => _files.ExecuteLineInput(session, context, lineInput),
            // MS-VBAL §5.4.5.10: Input # reads a field per variable and Let-assigns each.
            KeywordStatementNode { Token: Tokens.Input } input => _files.ExecuteInput(session, context, input),
            // MS-VBAL §5.4.5.3/.7: the statements that reposition a channel and set its line width.
            KeywordStatementNode { Token: Tokens.Seek } seek => _files.ExecuteSeek(session, context, seek),
            KeywordStatementNode { Token: Tokens.Width } width => _files.ExecuteWidth(session, context, width),
            // MS-VBAL §5.4.5.11-12: the record statements, which move bytes rather than characters.
            KeywordStatementNode { Token: Tokens.Put } put => _files.ExecutePut(session, context, put),
            KeywordStatementNode { Token: Tokens.Get } get => _files.ExecuteGet(session, context, get),
            // MS-VBAL §5.4.5.4-5: Lock and Unlock, which share a node because they share a record range.
            FileLockStatementNode fileLock => _files.ExecuteLock(session, context, fileLock),
            // MS-VBAL §5.4.2.20: invokes the procedures that handle an event of the object whose code this is.
            KeywordStatementNode { Token: Tokens.RaiseEvent } raise => ExecuteRaiseEvent(session, context, raise),
            CallStatementNode call => ExecuteCall(session, context, call),
            _ => RuntimeExecutionOutcome.InternalError,
        };

    // the parser gives the event's name as the first input, a bare name that is not an expression to evaluate, and the
    // arguments after it.
    private RuntimeExecutionOutcome ExecuteRaiseEvent(IRuntimeSession session, RuntimeEvaluationContext context, KeywordStatementNode raise)
    {
        if (raise.Inputs is not [SimpleNameExpressionNode eventName, ..var arguments])
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var result = _expressionEvaluator.RaiseEvent(session, context, eventName.IdentifierName, [.. arguments.OfType<ExpressionNode>()]);
        return result.IsSuccess
            ? RuntimeExecutionOutcome.Next
            : result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
    }

    // Debug.Assert: suspends execution when its expression is False, which is what Break means here -
    // the same outcome a Stop statement produces. An expression that cannot be coerced to Boolean is a
    // real run-time error, reported as one.
    private RuntimeExecutionOutcome ExecuteAssert(IRuntimeSession session, RuntimeEvaluationContext context, DebugAssertStatementNode assert)
    {
        var result = _conditions.EvaluateBoolean(session, assert.Condition, context);
        if (!result.IsSuccess)
        {
            return result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
        }

        return result.Result is VBBooleanValue { Value.StoredValue: 0 }
            ? RuntimeExecutionOutcome.Break
            : RuntimeExecutionOutcome.Next;
    }

    // MS-VBAL §5.4.2.1, and whatever the call returns is discarded - a Sub's own Void result discards
    // just as cleanly as a real one would.
    //
    // The two shapes differ in where the arguments are. `Call Foo(1, 2)` carries them inside the
    // Callee's own IndexExpressionNode, so evaluating the Callee is the whole call. The bare
    // `Foo 1, 2` has no parenthesized lExpression equivalent in the grammar, so its arguments are the
    // statement's own (see CallStatementNode) and the call is made from here - which is what S9a left
    // undone, on the ordinary VBA call form.
    private RuntimeExecutionOutcome ExecuteCall(IRuntimeSession session, RuntimeEvaluationContext context, CallStatementNode call)
    {
        var result = call.Arguments.IsEmpty
            ? _expressionEvaluator.Evaluate(session, call.Callee, context)
            : _expressionEvaluator.Invoke(session, context, call.Callee, call.Arguments);

        return result.IsSuccess ? RuntimeExecutionOutcome.Next
            : result.IsInternalError ? RuntimeExecutionOutcome.InternalError
            : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
    }

    // MS-VBAL §5.4.3.8. Evaluating the source expression is this statement's own business; assigning the
    // result into the target is LetAssignmentEvaluator's, which the file statements that Let-assign what
    // they read share with it.
    private RuntimeExecutionOutcome ExecuteLetAssignment(IRuntimeSession session, RuntimeEvaluationContext context, AssignmentStatementNode assignment)
    {
        var valueResult = _expressionEvaluator.Evaluate(session, assignment.Value, context);
        if (!valueResult.IsSuccess)
        {
            return valueResult.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(valueResult.ErrorInfo!);
        }

        return _assignments.Assign(session, context, assignment, assignment.Target, assignment.Value, valueResult.Result!);
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

        // a value is a view of its handle, which is about to be written to: what the variable held is its object's
        // identity as of now.
        var previous = target.ResolvedType.CreateValue(handle) is VBObjectValue held ? new VBObjectValue(held.Value) : null;

        // MS-VBAL §5.4.3.9: a WithEvents variable's handlers are detached from the object it holds before the
        // assignment, and attached to the object it is given after it.
        var variable = (Symbol)target;
        var withEvents = variable.GetProperty(SymbolProperties.WithEvents);
        if (withEvents)
        {
            EventAttachments.Detach(session, context, variable, previous);
        }

        handle.SetValue(session.Symbols.Resolver, coercionResult.Result!.RuntimeValue);

        // MS-VBAL §5.3.1.10: the object the variable held loses a reference, and Terminate runs when it was the last.
        // The variable already holds the new one by now, so a handler that reads it sees what the program wrote.
        ObjectReferences.Rebind(session, handle, previous, coercionResult.Result as VBObjectValue);

        if (withEvents)
        {
            EventAttachments.Attach(session, context, variable, coercionResult.Result as VBObjectValue);
        }

        return RuntimeExecutionOutcome.Next;
    }
}
