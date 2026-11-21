using RDCore.Runtime.Execution.Memory;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.Runtime.Compiled;

// what compiled code has the activation do with strings: the Mid statement on a String variable of the activation, and the text of a Single or a
// Double, written in the session's regional settings.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// The <c>Mid</c> statement of a <c>String</c> variable held in <paramref name="cell"/>, in characters (MS-VBAL §5.4.3.5): from
    /// <paramref name="start"/>, the next characters of the variable are replaced by the first characters of <paramref name="value"/>, as many as the least
    /// of the length, the characters left from <paramref name="start"/> and the characters of <paramref name="value"/>; the variable keeps its length. The
    /// characters are replaced where they are, in <paramref name="appender"/>, as MS-VBA replaces them in the <c>BSTR</c>: the builder is made of the
    /// String the cell holds when it does not hold it already, and the cell is given a String made of it only before something reads the variable
    /// (<see cref="StringOperations.WriteAppended"/>). No String is made for the statement, and no other variable that holds the String changes.
    /// </summary>
    /// <param name="appender">The activation's appender of the variable; <see langword="null"/> until the first append or <c>Mid</c> statement of it.</param>
    /// <param name="cell">The cell of the String variable.</param>
    /// <param name="start">The 1-based position of the first character replaced.</param>
    /// <param name="length">The most characters replaced, when <paramref name="hasLength"/>.</param>
    /// <param name="hasLength">Whether the statement gives a length.</param>
    /// <param name="value">The text the characters are taken from; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns>
    /// <see langword="false"/> when the interpreter runs the statement: a start below 1 or past the end, or a negative length, which is error 5; a cell that
    /// holds no text compiled code reads; or a String of the session in a <c>BSTR</c> (<c>StrPtr</c>), which the interpreter writes in place.
    /// </returns>
    public bool TryMid(ref TextAppender? appender, object? cell, long start, long length, bool hasLength, string? value)
    {
        if (FailsAccessAtRandom() || _session.Storage is SessionStorage { HasStringBlocks: true } || cell is not ValueBindingHandle handle)
        {
            return false;
        }

        appender ??= new TextAppender();
        if (!appender.Holds(handle))
        {
            if (!appender.TryTake(handle))
            {
                return false;
            }

            _body.CountTextCopied();
        }

        return appender.TryMid(handle, start, length, hasLength, value);
    }

    /// <summary>
    /// The <c>Mid</c> statement of a <c>String</c> parameter by reference held in <paramref name="cell"/>, as <see cref="TryMid"/> runs it, the cell given a
    /// new String made of the one it holds with the characters replaced: the parameter is the caller's variable, which other names can reach while
    /// the procedure runs (another parameter given the same variable, a module variable), so the cell holds the String the statement makes at once.
    /// </summary>
    /// <param name="cell">The cell of the String parameter.</param>
    /// <param name="start">The 1-based position of the first character replaced.</param>
    /// <param name="length">The most characters replaced, when <paramref name="hasLength"/>.</param>
    /// <param name="hasLength">Whether the statement gives a length.</param>
    /// <param name="value">The text the characters are taken from; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns><see langword="false"/> when the interpreter runs the statement, as <see cref="TryMid"/> says.</returns>
    public bool TryMidInCell(object? cell, long start, long length, bool hasLength, string? value)
    {
        if (FailsAccessAtRandom() || _session.Storage is SessionStorage { HasStringBlocks: true } || !StringOperations.TryRead(cell, out var held))
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
        _body.CountTextCopied();
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
