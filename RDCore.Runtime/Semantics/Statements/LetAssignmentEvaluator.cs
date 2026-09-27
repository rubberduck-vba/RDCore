using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
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
/// <param name="expressions">Evaluates the owner of a member-access target, which has to be in hand before
/// the field being assigned can be.</param>
public sealed class LetAssignmentEvaluator(
    ILetCoercionRuntimeSemanticsProvider coercions,
    IVerboseMessageBuilder formatter,
    RuntimeExpressionEvaluator expressions)
{
    private readonly BinaryLetAssignmentOperatorRuntimeSemantics _letAssignment = new(coercions, formatter);

    /// <summary>
    /// Resolves the symbol <paramref name="target"/> names.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Assign(IRuntimeSession, RuntimeEvaluationContext, StatementNode, Symbol, ExpressionNode, ExpressionNode, VBTypedValue)"/>
    /// because <c>Input #</c> (<strong>MS-VBAL §5.4.5.10</strong>) reads a <em>different number of characters</em>
    /// depending on the declared type of the variable it is about to assign — so it has to know the target
    /// before it has a value for it.
    /// </remarks>
    /// <param name="session">The session whose symbols the target is resolved against.</param>
    /// <param name="context">The evaluation context, whose scope the target is resolved in.</param>
    /// <param name="target">The variable expression.</param>
    /// <param name="symbol">The symbol it names.</param>
    /// <returns><c>false</c> when the expression is not a shape this can resolve, or names nothing.</returns>
    public bool TryResolveTarget(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode target, out Symbol? symbol)
    {
        symbol = target is SimpleNameExpressionNode simpleName
            ? session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope).Symbol
            : null;

        return symbol is not null;
    }

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
        => target is MemberAccessExpressionNode memberAccess
            ? AssignField(session, context, statement, memberAccess, source, value)
            : TryResolveTarget(session, context, target, out var symbol)
                ? Assign(session, context, statement, symbol!, target, source, value)
                : RuntimeExecutionOutcome.InternalError;

    // MS-VBAL §5.4.3.8 with a <member-access-expression> target whose owner is a UDT. A UDT field is not an
    // addressable Symbol the way a variable is - it lives on the value, which is what makes it reachable at
    // all - so the assignment is the coercion plus a write to the cell, rather than the "__let_op" operator
    // every Symbol-targeted assignment goes through. A class instance's field is still the operator's, since
    // it does have real storage; this only takes the UDT case.
    private RuntimeExecutionOutcome AssignField(
        IRuntimeSession session,
        RuntimeEvaluationContext context,
        StatementNode statement,
        MemberAccessExpressionNode memberAccess,
        ExpressionNode source,
        VBTypedValue value)
    {
        if (!TryEvaluateOwner(session, context, memberAccess, out var owner, out var failure))
        {
            return failure;
        }

        if (owner is not VBUserDefinedTypeValue udt)
        {
            // an Object/class field target, or a Property Let: neither is this method's, and neither is wired.
            return RuntimeExecutionOutcome.InternalError;
        }

        var name = memberAccess.Member.IdentifierName;
        if (udt.Fields.FirstOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            is not { ResolvedType: { } fieldType } declared)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        // "the source is Let-coerced to the target's declared type" - a field's declared type is its own, and
        // the coercion is the same one an assignment to a variable of that type would apply.
        var frame = new LetCoercionStackFrame(
            statement.Identity, InputIndex.CoercionSourceValue, value, new VBTypeDescValue(fieldType));
        var coerced = coercions.EvaluateLetCoercionSemantics(session.Symbols.Resolver, source, frame);
        if (!coerced.IsApplicable)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (!coerced.IsSuccess)
        {
            return RuntimeExecutionOutcome.Error(coerced.ErrorInfo!);
        }

        return udt.TrySetField(declared.Name, coerced.Result!)
            ? RuntimeExecutionOutcome.Next
            : RuntimeExecutionOutcome.InternalError;
    }

    // the owner of a member-access target, which is an expression in its own right: `a.b.c = 1` assigns a
    // field of whatever `a.b` is, so the owner is evaluated rather than resolved.
    private bool TryEvaluateOwner(
        IRuntimeSession session, RuntimeEvaluationContext context, MemberAccessExpressionNode memberAccess,
        out VBTypedValue? owner, out RuntimeExecutionOutcome failure)
    {
        owner = null;
        failure = RuntimeExecutionOutcome.Next;

        if (memberAccess.Owner is null)
        {
            // the With-relative form, whose owner is the enclosing With block's target.
            owner = context.EnclosingWithTarget;
            return owner is not null;
        }

        var evaluated = expressions.Evaluate(session, memberAccess.Owner, context);
        if (!evaluated.IsSuccess)
        {
            failure = evaluated.IsInternalError
                ? RuntimeExecutionOutcome.InternalError
                : RuntimeExecutionOutcome.Error(evaluated.ErrorInfo!);
            return false;
        }

        owner = evaluated.Result;
        while (owner is VBVariantValue { TypedValue: { } wrapped })
        {
            owner = wrapped;
        }

        return owner is not null;
    }

    /// <inheritdoc cref="Assign(IRuntimeSession, RuntimeEvaluationContext, StatementNode, ExpressionNode, ExpressionNode, VBTypedValue)"/>
    /// <param name="session">The session whose call stack a function result variable is assigned on.</param>
    /// <param name="context">The evaluation context, whose scope decides whether the target is the enclosing
    /// procedure's own result variable.</param>
    /// <param name="statement">The statement doing the assigning.</param>
    /// <param name="symbol">The already-resolved symbol <paramref name="target"/> names.</param>
    /// <param name="target">The variable expression being assigned into.</param>
    /// <param name="source">The expression the value came from, for the location of a coercion error.</param>
    /// <param name="value">The value to assign, already evaluated.</param>
    public RuntimeExecutionOutcome Assign(
        IRuntimeSession session,
        RuntimeEvaluationContext context,
        StatementNode statement,
        Symbol symbol,
        ExpressionNode target,
        ExpressionNode source,
        VBTypedValue value)
    {

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
