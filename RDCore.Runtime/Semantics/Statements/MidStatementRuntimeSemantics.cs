using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Facts;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// <strong>MS-VBAL §5.4.3.5</strong> the <c>Mid</c>, <c>MidB</c>, <c>Mid$</c> and <c>MidB$</c> statements — replace a
/// span of the characters (or bytes) of a variable with characters (or bytes) of a value, leaving the rest of it, and its
/// length, as they were.
/// </summary>
/// <remarks>
/// The replacement never makes the string longer: it is the least of the <c>length</c> asked for, what is left of the
/// target from <c>start</c> on, and what the value has to give. The <c>B</c> forms count <em>bytes</em> of the string's
/// in-memory form - two to a character, as <c>LenB</c> does, and the byte left over of a string whose data is an odd
/// number of bytes - so that an odd position splits a character, which is the point of them. The other forms count and
/// replace whole characters through the same bytes, two to a character, so the byte left over is not one of the
/// characters they count and stays where it is. The trailing <c>$</c> changes nothing at run time.
/// <para>
/// The new string is Let-assigned to the target like any other value (<strong>§5.4.3.8</strong>), so the target can be any
/// variable expression an assignment accepts, and a fixed-length <c>String</c> or a <c>Byte()</c> target is given back in
/// its own shape.
/// </para>
/// </remarks>
/// <param name="Expressions">Evaluates the target and the operands.</param>
/// <param name="LetCoercion">Let-coerces the target and the value to <c>String</c>, which the statement requires of both, and the position and the length to <c>Long</c>.</param>
/// <param name="Assignments">Let-assigns the new string into the target.</param>
public sealed record class MidStatementRuntimeSemantics(
    RuntimeExpressionEvaluator Expressions,
    ILetCoercionRuntimeSemanticsProvider LetCoercion,
    LetAssignmentEvaluator Assignments)
{
    /// <summary>
    /// Executes a <c>Mid</c> statement.
    /// </summary>
    /// <param name="session">The session the statement runs in.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="mid">The statement.</param>
    public RuntimeExecutionOutcome Execute(IRuntimeSession session, RuntimeEvaluationContext context, MidStatementNode mid)
    {
        if (!TryEvaluateString(session, context, mid, mid.Target, out var target, out var failure)
            || !TryEvaluateLong(session, context, mid.Start, out var start, out failure)
            || !TryEvaluateOptionalLong(session, context, mid.Length, out var length, out failure)
            || !TryEvaluateString(session, context, mid, mid.Value, out var value, out failure))
        {
            return failure;
        }

        // "If the value of <start> is less than or equal to 0 or greater than the length of <string-argument>, or if
        // <length> is less than 0, runtime error 5" - the length being the one the mode counts in.
        var targetLength = mid.IsByteMode ? target!.ByteLength : Text(target!).Length;
        if (start <= 0 || start > targetLength || length < 0)
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.InvalidProcedureCallOrArgument, mid.SourceLocation,
                $"{(mid.IsByteMode ? "MidB" : "Mid")}: start {start}, length {(length?.ToString() ?? "omitted")}, in a string of {targetLength}"));
        }

        var replaced = mid.IsByteMode
            ? ReplaceBytes(target, (int)start - 1, length, value!)
            : ReplaceBytes(target, 2 * ((int)start - 1), 2 * CharactersReplaced(target, (int)start - 1, length, value!), value!);

        // the characters are written where they are: a String whose characters are in a BSTR (StrPtr) is in the same one once it is assigned.
        var storage = session.Storage as Execution.Memory.SessionStorage;
        var inPlace = storage?.BlockOf(target.RuntimeValue);
        var assigned = Assignments.Assign(session, context, mid, mid.Target, mid.Value, replaced);
        if (inPlace is not null)
        {
            storage!.KeepInPlace(inPlace);
        }

        return assigned;
    }

    private static string Text(VBStringValue value) => value.Handle.Value.BoxedValue as string ?? string.Empty;

    // "the next x characters within <string-argument> are replaced by the first x characters of v, where x = the least
    // of the following: <length>, the number of characters in <string-argument> after and including the first character
    // to replace, or the number of characters in v." Those are whole characters of both, two bytes each, and they are
    // replaced as the bytes they are: what follows the target's last character - the byte left over of a string whose
    // data is an odd number of bytes - is not one of them, and is left as it is.
    private static long CharactersReplaced(VBStringValue target, int offset, long? length, VBStringValue value)
        => Math.Min(Math.Min(length ?? int.MaxValue, Text(target).Length - offset), Text(value).Length);

    // the same in bytes: the string's data as it is stored - UTF-16 little-endian code units, and the byte left over of an
    // odd one - so a byte offset can fall inside a character. They are the string's own bytes (VBStringValue.GetBytes),
    // not a text encoding of its characters, which would read the lone surrogate an odd offset can make as U+FFFD.
    private static VBStringValue ReplaceBytes(VBStringValue target, int offset, long? length, VBStringValue value)
    {
        var bytes = target.GetBytes();
        var source = value.GetBytes();
        var count = (int)Math.Min(Math.Min(length ?? int.MaxValue, bytes.Length - offset), source.Length);
        Array.Copy(source, 0, bytes, offset, count);
        return VBStringValue.FromBytes(bytes);
    }

    // "The data value of <string-argument> MUST be Let-coercible to String", and so must the value assigned into it.
    private bool TryEvaluateString(
        IRuntimeSession session, RuntimeEvaluationContext context, MidStatementNode mid, ExpressionNode expression,
        [MaybeNullWhen(false)][NotNullWhen(true)] out VBStringValue? text, [MaybeNullWhen(false)][NotNullWhen(true)] out RuntimeExecutionOutcome failure)
    {
        text = null;
        var evaluated = Expressions.Evaluate(session, expression, context);
        if (!evaluated.IsSuccess)
        {
            failure = ToFailure(evaluated);
            return false;
        }

        // a Variant is what it holds, as far as the coercion is concerned.
        var source = evaluated.Result!;
        while (source is VBVariantValue { TypedValue: { } wrapped })
        {
            source = wrapped;
        }

        // a String nothing observes is let-coerced by its plan (IR lowering S0.11): a copy of it.
        if (Expressions.TryConvertUnobserved(source, VBStringType.TypeInfo, out var planned) && planned is VBStringValue plannedText)
        {
            text = plannedText;
            failure = RuntimeExecutionOutcome.Next;
            return true;
        }

        var coerced = LetCoercion.EvaluateLetCoercionSemantics(session.Symbols.Resolver, expression, new()
        {
            NodeId = mid.Identity,
            SourceValue = source,
            DestinationTypeDesc = new(VBStringType.TypeInfo),
            Site = ConversionSite.StringStatement,
        });

        if (!coerced.IsSuccess)
        {
            failure = coerced.IsApplicable
                ? RuntimeExecutionOutcome.Error(coerced.ErrorInfo!)
                : RuntimeExecutionOutcome.InternalError;
            return false;
        }

        text = (VBStringValue)coerced.Result!;
        failure = RuntimeExecutionOutcome.Next;
        return true;
    }

    // "start = integer-expression", "length = integer-expression": Let-coerced to Long, which is what the Mid function takes.
    private bool TryEvaluateLong(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression,
        out long value, out RuntimeExecutionOutcome failure)
    {
        value = 0;
        var evaluated = Expressions.Evaluate(session, expression, context);
        if (!evaluated.IsSuccess)
        {
            failure = ToFailure(evaluated);
            return false;
        }

        // a number nothing observes is let-coerced by its plan (IR lowering S0.11); one that does not fit a Long is the coercion's to raise.
        if (Expressions.TryConvertUnobserved(evaluated.Result!, VBLongType.TypeInfo, out var planned) && planned is VBLongValue plannedLong)
        {
            value = plannedLong.Value;
            failure = RuntimeExecutionOutcome.Next;
            return true;
        }

        var coerced = LetCoercion.EvaluateLetCoercionSemantics(session.Symbols.Resolver, expression, new()
        {
            NodeId = expression.Identity,
            SourceValue = evaluated.Result!,
            DestinationTypeDesc = new(VBLongType.TypeInfo),
            Site = ConversionSite.StringStatement,
        });

        if (!coerced.IsSuccess)
        {
            failure = RuntimeExecutionOutcome.Error(coerced.ErrorInfo!);
            return false;
        }

        value = Convert.ToInt64(coerced.Result!.Handle.Value.BoxedValue);
        failure = RuntimeExecutionOutcome.Next;
        return true;
    }

    private bool TryEvaluateOptionalLong(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode? expression,
        out long? value, out RuntimeExecutionOutcome failure)
    {
        value = null;
        if (expression is null)
        {
            failure = RuntimeExecutionOutcome.Next;
            return true;
        }

        if (!TryEvaluateLong(session, context, expression, out var length, out failure))
        {
            return false;
        }

        value = length;
        return true;
    }

    private static RuntimeExecutionOutcome ToFailure(RuntimeSemanticsEvaluationResult result)
        => result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
}
