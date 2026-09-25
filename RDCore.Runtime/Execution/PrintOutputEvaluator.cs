using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using System.Collections.Immutable;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Writes an output list to the session's output (<strong>MS-VBAL §5.4.5.8</strong> and its
/// <strong>§5.4.5.8.1</strong> output-list grammar).
/// </summary>
/// <remarks>
/// The spec writes these rules against a file number and a <em>current line position</em>, and they
/// are the same rules whether the destination is a file or the <c>Immediate</c> window — which is
/// why the position belongs to <see cref="IRuntimeOutput"/> and the formatting belongs here. The
/// clauses that only exist because a file has a maximum line length (wrapping mid-string, the
/// <c>Spc</c>/<c>Tab</c> modulo cases) do not apply to a session's output, which has none.
/// </remarks>
/// <param name="expressionEvaluator">Evaluates each output expression.</param>
/// <param name="stringCoercion">Let-coerces an evaluated value to <c>String</c>, per the output-string rules.</param>
/// <param name="numericCoercion">Let-coerces a <c>Spc</c>/<c>Tab</c> count to <c>Long</c>.</param>
public sealed class PrintOutputEvaluator(
    RuntimeExpressionEvaluator expressionEvaluator,
    VBStringLetCoercionRuntimeSemantics stringCoercion,
    VBNumericLetCoercionTypeRuntimeSemantics numericCoercion)
{
    /// <summary>
    /// The width of a print zone (<strong>MS-VBAL §5.4.5</strong>: "divided into a sequence of
    /// fourteen-character wide print zones").
    /// </summary>
    private const int PrintZoneWidth = 14;

    /// <summary>
    /// Writes <paramref name="items"/> to <paramref name="session"/>'s output.
    /// </summary>
    /// <param name="session">The session whose output is written to.</param>
    /// <param name="context">The runtime context each output expression is evaluated against.</param>
    /// <param name="items">The output list, in source order; empty writes a blank line.</param>
    public RuntimeExecutionOutcome Execute(IRuntimeSession session, RuntimeEvaluationContext context, ImmutableArray<PrintOutputItemNode> items)
    {
        var output = session.Output;
        if (items.IsEmpty)
        {
            // "If <output-list> is not present, the line termination sequence ... is written".
            output.WriteLine();
            return RuntimeExecutionOutcome.Next;
        }

        foreach (var item in items)
        {
            if (WriteItem(session, context, output, item) is { } failed)
            {
                return failed;
            }
        }

        // "If the <char-position> of the last <output-item> is neither a ',' or an explicitly
        // occurring ';' the ... line termination sequence is output" — so either separator leaves the
        // line open for whatever prints next.
        if (items[^1].Separator is null)
        {
            output.WriteLine();
        }

        return RuntimeExecutionOutcome.Next;
    }

    // returns null when the item was written, or the outcome that stopped it.
    private RuntimeExecutionOutcome? WriteItem(IRuntimeSession session, RuntimeEvaluationContext context, IRuntimeOutput output, PrintOutputItemNode item)
    {
        switch (item.Value)
        {
            case null:
                // a bare separator: nothing is printed before the column move below.
                break;

            case PrintSpcClauseNode spc:
                if (Evaluate(session, context, spc.Count) is not { } spaces)
                {
                    return RuntimeExecutionOutcome.InternalError;
                }
                if (spaces.Outcome is { } spcFailure)
                {
                    return spcFailure;
                }
                output.Write(new string(' ', Math.Max(0, spaces.Count)));
                break;

            case PrintTabClauseNode { Column: null }:
                // bare Tab: "advanced to the next print zone ... until (current line position modulo 14) equals 1".
                AdvanceToNextPrintZone(output);
                break;

            case PrintTabClauseNode tab:
                if (Evaluate(session, context, tab.Column!) is not { } column)
                {
                    return RuntimeExecutionOutcome.InternalError;
                }
                if (column.Outcome is { } tabFailure)
                {
                    return tabFailure;
                }
                // "If t less than or equal to the current line position, output the line termination
                // sequence", then pad out to column t.
                if (column.Count <= output.LinePosition)
                {
                    output.WriteLine();
                }
                output.Write(new string(' ', Math.Max(0, column.Count - output.LinePosition)));
                break;

            default:
                var result = expressionEvaluator.Evaluate(session, item.Value, context);
                if (!result.IsSuccess)
                {
                    return result.IsInternalError
                        ? RuntimeExecutionOutcome.InternalError
                        : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
                }

                if (ToOutputString(session, item.Value, result.Result!) is not { } text)
                {
                    return RuntimeExecutionOutcome.InternalError;
                }
                output.Write(text);
                break;
        }

        if (item.Separator == ",")
        {
            AdvanceToNextPrintZone(output);
        }

        return null;
    }

    // "outputting space characters until (current line position modulo 14) equals 1. ... Note that the
    // print zone is advanced even if the current file-pointer-position is already at the beginning of
    // a print zone" — so a zone boundary always moves a whole zone on, never stays put.
    private static void AdvanceToNextPrintZone(IRuntimeOutput output)
    {
        var column = output.LinePosition - 1;
        output.Write(new string(' ', PrintZoneWidth - (column % PrintZoneWidth)));
    }

    /// <summary>
    /// The output string of an output expression, per <strong>MS-VBAL §5.4.5.8</strong>'s own list of
    /// cases. Returns <c>null</c> when the value cannot be Let-coerced to <c>String</c> at all.
    /// </summary>
    private string? ToOutputString(IRuntimeSession session, ExpressionNode expression, VBTypedValue value)
    {
        // a Variant's own wrapped value is what is printed; the wrapper never is.
        while (value is VBVariantValue { TypedValue: var wrapped })
        {
            value = wrapped;
        }

        return value switch
        {
            VBBooleanValue boolean => boolean.Value.StoredValue != 0 ? "True" : "False",
            VBNullValue => "Null",
            // "the output string is 'Error ' followed by the error code Let-coerced to String".
            VBErrorValue error => $"Error {error.Value}",
            // a Date is numeric but is excluded from the space-padded numeric case by name.
            VBDateValue => Coerce(session, expression, value),
            // "with a space character inserted as the first and the last character of the String".
            VBNumericTypedValue => Coerce(session, expression, value) is { } number ? $" {number} " : null,
            _ => Coerce(session, expression, value),
        };
    }

    private string? Coerce(IRuntimeSession session, ExpressionNode expression, VBTypedValue value)
    {
        var frame = new LetCoercionStackFrame(expression.Identity, InputIndex.CoercionSourceValue, value, new VBTypeDescValue(VBStringType.TypeInfo));
        var result = stringCoercion.EvaluateLetCoercion(session.Symbols.Resolver, expression, frame);
        return result is { IsApplicable: true, IsSuccess: true, Result: VBStringValue coerced } ? coerced.Value : null;
    }

    // a Spc/Tab count: evaluated, then Let-coerced to Long so a Variant or a Double argument works.
    private (int Count, RuntimeExecutionOutcome? Outcome)? Evaluate(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression)
    {
        var result = expressionEvaluator.Evaluate(session, expression, context);
        if (!result.IsSuccess)
        {
            return (0, result.IsInternalError
                ? RuntimeExecutionOutcome.InternalError
                : RuntimeExecutionOutcome.Error(result.ErrorInfo!));
        }

        var source = result.Result!;
        while (source is VBVariantValue { TypedValue: var wrapped })
        {
            source = wrapped;
        }

        var frame = new LetCoercionStackFrame(expression.Identity, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(VBLongType.TypeInfo));
        var coerced = numericCoercion.EvaluateLetCoercion(session.Symbols.Resolver, expression, frame);
        if (!coerced.IsApplicable)
        {
            return null;
        }

        return coerced is { IsSuccess: true, Result: VBLongValue count }
            ? (count.Value, null)
            : (0, RuntimeExecutionOutcome.Error(coerced.ErrorInfo!));
    }
}
