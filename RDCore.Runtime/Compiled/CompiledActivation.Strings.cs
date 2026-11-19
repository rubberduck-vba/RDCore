using RDCore.Runtime.Execution.Memory;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.Runtime.Compiled;

// what compiled code does to a String variable of the activation besides assigning it a value: the Mid statement; and the text of a Single or a Double,
// which the session's regional settings write.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// The <c>Mid</c> statement of a <c>String</c> variable held in <paramref name="cell"/>, in characters (MS-VBAL §5.4.3.5): from
    /// <paramref name="start"/>, the next characters of the variable are replaced by the first characters of <paramref name="value"/>, as many as the least
    /// of the length, the characters left from <paramref name="start"/> and the characters of <paramref name="value"/>; the variable keeps its length. The
    /// characters are replaced in <paramref name="appender"/> while it holds what was appended to the variable that the cell does not hold; otherwise
    /// in the String the cell holds, after what the appender holds is written into it.
    /// </summary>
    /// <param name="appender">The activation's appender of the variable; <see langword="null"/> for a variable never appended to.</param>
    /// <param name="cell">The cell of the String variable.</param>
    /// <param name="start">The 1-based position of the first character replaced.</param>
    /// <param name="length">The most characters replaced, when <paramref name="hasLength"/>.</param>
    /// <param name="hasLength">Whether the statement gives a length.</param>
    /// <param name="value">The text the characters are taken from; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns>
    /// <see langword="false"/> when the interpreter runs the statement: a start below 1 or past the end, or a negative length, which is error 5; a cell that
    /// holds no text compiled code reads; or a String of the session in a <c>BSTR</c> (<c>StrPtr</c>), which the interpreter writes in place.
    /// </returns>
    public bool TryMid(TextAppender? appender, object? cell, long start, long length, bool hasLength, string? value)
    {
        if (FailsAccessAtRandom() || _session.Storage is SessionStorage { HasStringBlocks: true })
        {
            return false;
        }

        if (appender is not null && cell is ValueBindingHandle handle && appender.TryMid(handle, start, length, hasLength, value))
        {
            return true;
        }

        StringOperations.WriteAppended(appender, cell);
        if (!StringOperations.TryRead(cell, out var held))
        {
            return false;
        }

        var text = held ?? string.Empty;
        if (StringOperations.MidReplaced(text.Length, start, length, hasLength, value) is not var (index, replaced))
        {
            return false;
        }

        if (replaced == 0)
        {
            return true;
        }

        var made = string.Create(text.Length, (text, value: value!, index, replaced), static (characters, state) =>
        {
            state.text.AsSpan().CopyTo(characters);
            state.value.AsSpan(0, state.replaced).CopyTo(characters[state.index..]);
        });
        return StringOperations.Write(cell, made);
    }

    /// <summary>
    /// A <c>Single</c> or a <c>Double</c> let-coerced to <c>String</c> (MS-VBAL §5.5.1.2.4), as the interpreter's let-coercion writes it: with the decimal
    /// symbol of the regional settings of the session's environment (<see cref="ValueConversions.FloatingToText"/>).
    /// </summary>
    /// <param name="value">The value; a <c>Single</c> is held as a <see cref="double"/>.</param>
    /// <param name="isSingle">Whether the value is a <c>Single</c>.</param>
    public string FloatingText(double value, bool isSingle)
        => ValueConversions.FloatingToText(value, isSingle, ValueConversions.DecimalSymbolOf(_session.Environment.Culture, _session.Environment.Regional));
}
