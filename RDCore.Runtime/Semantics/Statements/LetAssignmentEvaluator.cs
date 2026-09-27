using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// Let-assigns a value that has already been evaluated into a variable expression
/// (<strong>MS-VBAL 5.4.3.8</strong>).
/// </summary>
/// <remarks>
/// An assignment statement is not the only thing that Let-assigns. <c>Line Input #</c> Let-assigns the line
/// it read, <c>Input #</c> Let-assigns each value in its list, and <c>Get</c> Let-assigns the record it read
/// (MS-VBAL 5.4.5.6, .10 and .12, each saying so in those words) - so the target side of an assignment is
/// factored out here rather than restated by each of them, and the function-result-variable rule below is
/// honoured by all of them for free.
/// <para>
/// Scoped to a target that resolves to a plain <see cref="Symbol"/>, same as
/// <see cref="BinaryLetAssignmentOperatorRuntimeSemantics"/> itself documents: a member-access or indexed
/// target needs procedure-invocation machinery that doesn't exist yet.
/// </para>
/// </remarks>
/// <param name="coercions">The Let-coercion rules the assignment applies to its source value.</param>
/// <param name="formatter">Formats the verbose message of an error the coercion raises.</param>
public sealed class LetAssignmentEvaluator(ILetCoercionRuntimeSemanticsProvider coercions, IVerboseMessageBuilder formatter)
{
    private readonly BinaryLetAssignmentOperatorRuntimeSemantics _letAssignment = new(coercions, formatter);

    /// <summary>
    /// Let-assigns <paramref name="value"/> into <paramref name="target"/>.
    /// </summary>
    /// <param name="session">The session whose symbols the target is resolved against.</param>
    /// <param name="context">The evaluation context, whose scope the target is resolved in.</param>
    /// <param name="statement">The statement doing the assigning, whose identity and location the
    /// synthetic assignment operator takes.</param>
    /// <param name="target">The variable expression being assigned into.</param>
    /// <param name="source">The expression the value came from, for the location of a coercion error. The
    /// <paramref name="target"/> itself when the value came from somewhere that is not an expression.</param>
    /// <param name="value">The value to assign, already evaluated.</param>
    public RuntimeExecutionOutcome Assign(
        IRuntimeSession session,
        RuntimeEvaluationContext context,
        StatementNode statement,
        ExpressionNode target,
        ExpressionNode source,
        VBTypedValue value)
    {
        if (target is not SimpleNameExpressionNode simpleName)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var targetResult = session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope);
        if (targetResult.Symbol is not { } symbol)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (symbol is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol
            && symbol.Uri.AbsoluteUri == context.Scope.AbsoluteUri && session.CallStack.Current is { } enclosing)
        {
            // MS-VBAL 5.3.1: "Foo = value" inside Foo's own body Let-assigns its function result
            // variable, not the general symbol table - the read-side mirror of this check is
            // RuntimeExpressionEvaluator.EvaluateSimpleName's own self-reference check. The function
            // result variable isn't a real addressable Symbol, so this can't go through the same
            // "__let_op" operator every other target does (it needs a real IBindingHandle) - Let-coerce
            // directly instead, the same lower-level call ByVal/ByRef-fallback parameter passing already
            // makes for the identical reason.
            var returnCoercionFrame = new LetCoercionStackFrame(statement.Identity, InputIndex.CoercionSourceValue,
                value, new VBTypeDescValue(((ITypedSymbol)symbol).ResolvedType));
            var returnCoercionResult = coercions.EvaluateLetCoercionSemantics(session.Symbols.Resolver, source, returnCoercionFrame);
            if (!returnCoercionResult.IsApplicable)
            {
                return RuntimeExecutionOutcome.InternalError;
            }

            if (!returnCoercionResult.IsSuccess)
            {
                return RuntimeExecutionOutcome.Error(returnCoercionResult.ErrorInfo!);
            }

            ((CallStackFrame)enclosing).ReturnValue = returnCoercionResult.Result!;
            return RuntimeExecutionOutcome.Next;
        }

        // the reserved synthetic "__let_op" binary operator - the same shape its own test suite
        // exercises it with: a throwaway node carrying this statement's own identity/location, operands
        // passed directly rather than read back off the node's Children.
        var syntheticOperator = new VBBinaryOperatorExpressionNode(
            OperatorSymbolNames.BinaryAssignmentValueOp, statement.Identity, statement.SourceLocation, target, source);
        var result = _letAssignment.Evaluate(session, new(), syntheticOperator, new VBSymbolDescValue(symbol), value);

        return result.IsSuccess ? RuntimeExecutionOutcome.Next
            : result.IsInternalError ? RuntimeExecutionOutcome.InternalError
            : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
    }
}
