using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;
using System.Globalization;

namespace RDCore.Runtime.Execution;

/// <summary>
/// <strong>MS-VBAL §5.4.5.9</strong> the <c>Write #</c> statement's output list.
/// </summary>
/// <remarks>
/// Not a variation on <see cref="PrintOutputEvaluator"/>, despite sharing the output-list grammar: what
/// <c>Write</c> produces is a <em>machine-readable</em> record that <c>Input #</c> reads back, so every value
/// is written in a form that survives the round trip. Strings are quoted, so a comma inside one is not
/// mistaken for a separator; numbers use <c>.</c> as the decimal separator whatever the locale, so a file
/// written in one region reads in another; and <c>True</c>, <c>Null</c>, an <c>Error</c> and a <c>Date</c>
/// each get a delimited spelling of their own, because none of them has an unambiguous plain text.
/// <para>
/// 👉 Those spellings — <c>#TRUE#</c>, <c>#NULL#</c>, <c>#yyyy-mm-dd hh:mm:ss#</c> — are not VBA source
/// syntax (<strong>§3.3</strong>'s <c>boolean-literal-identifier</c> is only <c>true</c>/<c>false</c>). They
/// exist for this file format and for the coercion that reads them back
/// (<strong>§5.5.1.2</strong> accepts <c>"#TRUE#"</c> case-sensitively).
/// </para>
/// </remarks>
/// <param name="Expressions">Evaluates each output expression.</param>
public sealed record class WriteOutputEvaluator(RuntimeExpressionEvaluator Expressions)
{
    /// <summary>
    /// VBA's date origin: serial <c>0</c>. A value on this date is a time of day and nothing more, and a
    /// value at midnight is a date and nothing more — which is what lets one spelling carry both.
    /// </summary>
    private static readonly DateTime DateOrigin = new(1899, 12, 30);

    /// <summary>
    /// Writes <paramref name="items"/> to <paramref name="output"/>.
    /// </summary>
    /// <param name="session">The session the expressions are evaluated against.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="items">The output list, in source order.</param>
    /// <param name="output">Where the characters go.</param>
    public RuntimeExecutionOutcome Execute(
        IRuntimeSession session, RuntimeEvaluationContext context,
        ImmutableArray<PrintOutputItemNode> items, IRuntimeOutput output)
    {
        foreach (var item in items)
        {
            if (item.Value is not null)
            {
                var evaluated = Expressions.Evaluate(session, item.Value, context);
                if (!evaluated.IsSuccess)
                {
                    return evaluated.IsInternalError
                        ? RuntimeExecutionOutcome.InternalError
                        : RuntimeExecutionOutcome.Error(evaluated.ErrorInfo!);
                }

                output.Write(OutputString(evaluated.Result!));
            }

            // a char-position between two values is an <output-item> of its own (the grammar's `outputItem :
            // charPosition` alternative), so `Write #1, "a", "b"` is three items and the comma is one of them.
            // Both ',' and ';' write a comma, because a Write record is comma-separated whichever the source
            // used - the separator only decides whether the record ends here (below).
            if (item.Separator is "," or ";")
            {
                output.Write(",");
            }
        }

        // "If the <char-position> of the last <output-item> is neither a ',' nor an explicitly occurring ';'
        // the implementation-defined line termination sequence is output" - so either separator holds the
        // record open, exactly as it does for Print.
        if (items.IsDefaultOrEmpty || items[^1].Separator is not ("," or ";"))
        {
            output.WriteLine();
        }

        return RuntimeExecutionOutcome.Next;
    }

    // MS-VBAL 5.4.5.9's own "output string value of an <output-expression>" rules, in the order it gives them.
    private string OutputString(VBTypedValue value) => Unwrapped(value) switch
    {
        VBBooleanValue boolean => IsTrue(boolean) ? "#TRUE#" : "#FALSE#",
        VBNullValue => "#NULL#",
        VBErrorValue error => $"#ERROR {Number(error)}#",
        VBStringValue text => $"\"{Text(text)}\"",
        VBDateValue date => DateString(date),
        // "any numeric data value other than a Date... Let-coerced to String ignoring any implementation
        // dependent locale setting and using '.' as the decimal separator" - a record written under one set of
        // regional settings has to read under another.
        VBNumericTypedValue number => Invariant(number),
        // "Otherwise, the output string is... Let-coerced to String with the data value of the string
        // surrounded with double quote characters."
        var other => $"\"{Coerced(other)}\"",
    };

    // "#yyyy-mm-dd hh:mm:ss#. Hours are specified in 24-hour form." A value at the origin date is a time and
    // nothing more; a value at midnight is a date and nothing more; the origin at midnight is a date.
    private static string DateString(VBDateValue value)
    {
        var moment = DateTime.FromOADate(Convert.ToDouble(value.Handle.Value.BoxedValue));
        var isOriginDate = moment.Date == DateOrigin;
        var isMidnight = moment.TimeOfDay == TimeSpan.Zero;

        return (isOriginDate, isMidnight) switch
        {
            (true, true) => $"#{DateOrigin:yyyy-MM-dd}#",
            (true, false) => $"#{moment:HH:mm:ss}#",
            (false, true) => $"#{moment:yyyy-MM-dd}#",
            _ => $"#{moment:yyyy-MM-dd HH:mm:ss}#",
        };
    }

    // a Variant's own TypeInfo mirrors whatever it wraps, so a pattern match on it would answer for the
    // wrapped value's type while the value in hand is still the Variant. Unwrap first, always.
    private static VBTypedValue Unwrapped(VBTypedValue value)
        => value is VBVariantValue && value.Handle.Value.BoxedValue is VBTypedValue wrapped ? wrapped : value;

    private static bool IsTrue(VBBooleanValue value) => Convert.ToBoolean(value.Handle.Value.BoxedValue);

    private static int Number(VBErrorValue value) => Convert.ToInt32(value.Handle.Value.BoxedValue);

    private static string Text(VBStringValue value) => value.Handle.Value.BoxedValue as string ?? string.Empty;

    private static string Invariant(VBNumericTypedValue value)
        => value.Handle.Value.BoxedValue is IConvertible convertible
            ? convertible.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

    private string Coerced(VBTypedValue value) => value.Handle.Value.BoxedValue?.ToString() ?? string.Empty;
}
