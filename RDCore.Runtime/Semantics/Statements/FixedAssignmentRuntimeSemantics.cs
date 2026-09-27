using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// <strong>MS-VBAL §5.4.3.6-7</strong> the <c>LSet</c> and <c>RSet</c> statements — assignment into a target
/// of a <em>fixed width</em>, which is what makes them two statements rather than one more
/// <see cref="AssignmentKind"/> of an ordinary assignment.
/// </summary>
/// <remarks>
/// Neither statement lets the target change size. Both read the width off the target's <em>current</em> value
/// and fit the source into exactly that many characters — padding with spaces when it is short, truncating
/// when it is long — differing only in which end the padding goes: <c>LSet</c> left-aligns, <c>RSet</c> right-
/// aligns. That the width comes from the value rather than from the declared type is what makes them useful on
/// a fixed-length <c>String</c>, and harmless on a variable-length one.
/// <para>
/// <c>LSet</c> has a second, unrelated job: between two UDT variables it is a <em>byte</em> copy
/// (<see cref="VBUserDefinedTypeImage"/>), which is how VBA fakes a union. <c>RSet</c> has no such form —
/// §5.4.3.7 admits only <c>String</c> and <c>Variant</c>.
/// </para>
/// </remarks>
/// <param name="Expressions">Evaluates the source expression.</param>
/// <param name="Strings">Let-coerces it to <c>String</c>, which both statements require of it.</param>
/// <param name="Assignments">Let-assigns the fitted string into the target.</param>
public sealed record class FixedAssignmentRuntimeSemantics(
    RuntimeExpressionEvaluator Expressions,
    VBStringLetCoercionRuntimeSemantics Strings,
    LetAssignmentEvaluator Assignments)
{
    /// <summary>The character a short value is padded with — U+0020, as the specification spells it.</summary>
    private const char Padding = ' ';

    /// <summary>
    /// Executes an <c>LSet</c> or <c>RSet</c> statement.
    /// </summary>
    /// <param name="session">The session the statement runs in.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="assignment">The statement, whose <see cref="AssignmentStatementNode.Kind"/> says which
    /// end the padding goes.</param>
    public RuntimeExecutionOutcome Execute(
        IRuntimeSession session, RuntimeEvaluationContext context, AssignmentStatementNode assignment)
    {
        if (!Assignments.TryResolveTarget(session, context, assignment.Target, out var symbol))
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var current = ((SDK.Model.Symbols.Abstract.ITypedSymbol)symbol!).ResolvedType
            .CreateValue(session.Symbols.Resolver.GetValue(symbol!));

        var evaluated = Expressions.Evaluate(session, assignment.Value, context);
        if (!evaluated.IsSuccess)
        {
            return evaluated.IsInternalError
                ? RuntimeExecutionOutcome.InternalError
                : RuntimeExecutionOutcome.Error(evaluated.ErrorInfo!);
        }

        // "The value type of <bound-variable-expression> MUST be String or a UDT" - a target holding anything
        // else is a type mismatch at run time, the static rule having already allowed String, Variant and UDT
        // through on the declared type alone.
        return (Unwrapped(current), Unwrapped(evaluated.Result!)) switch
        {
            (VBUserDefinedTypeValue target, VBUserDefinedTypeValue source)
                when assignment.Kind is AssignmentKind.LSet => CopyRecord(source, target),

            (VBStringValue target, _) => Fit(session, context, assignment, symbol!, target, evaluated.Result!),

            _ => Failed(assignment, VBRuntimeErrorId.TypeMismatch),
        };
    }

    // "The data in <expression>... is copied into <bound-variable-expression> variable" - bytes, not fields,
    // so that a copy between two differently-shaped records reinterprets rather than refusing. The target is
    // filled in place: a UDT has location identity, and LSet assigns no new one.
    private static RuntimeExecutionOutcome CopyRecord(VBUserDefinedTypeValue source, VBUserDefinedTypeValue target)
    {
        VBUserDefinedTypeImage.Copy(source, target);
        return RuntimeExecutionOutcome.Next;
    }

    private RuntimeExecutionOutcome Fit(
        IRuntimeSession session, RuntimeEvaluationContext context, AssignmentStatementNode assignment,
        SDK.Model.Symbols.Abstract.Symbol symbol, VBStringValue target, VBTypedValue source)
    {
        // "Let e be the data value of <expression> Let-coerced to declared type String."
        var coerced = Strings.EvaluateLetCoercion(session.Symbols.Resolver, assignment.Value, new()
        {
            NodeId = assignment.Identity,
            SourceValue = source,
            DestinationTypeDesc = new(VBStringType.TypeInfo),
        });

        if (!coerced.IsSuccess)
        {
            return RuntimeExecutionOutcome.Error(coerced.ErrorInfo!);
        }

        var text = coerced.Result!.Handle.Value.BoxedValue as string ?? string.Empty;

        // "Let qLength be the number of characters in the data value of <bound-variable-expression>" - the
        // target's own current length, which is the whole point: neither statement resizes anything.
        var width = target.Length;
        var fitted = text.Length >= width
            // both statements truncate from the same end: "the initial qLength characters of e" for LSet, and
            // "the first qLength characters in <expression>" for RSet. Only the padding side differs.
            ? text[..width]
            : assignment.Kind is AssignmentKind.RSet
                ? text.PadLeft(width, Padding)
                : text.PadRight(width, Padding);

        return Assignments.Assign(
            session, context, assignment, symbol, assignment.Target, assignment.Value, new VBStringValue(fitted));
    }

    // a Variant target or source is one of the declared types the static rule admits, and what it holds is
    // what the runtime rule is about - so both sides are unwrapped before either is judged.
    private static VBTypedValue Unwrapped(VBTypedValue value)
    {
        while (value is VBVariantValue { TypedValue: { } wrapped })
        {
            value = wrapped;
        }

        return value;
    }

    private static RuntimeExecutionOutcome Failed(AssignmentStatementNode assignment, VBRuntimeErrorId error)
        => RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
            error, assignment.SourceLocation, $"{assignment.Kind} requires a String or a user-defined type"));
}
