using RDCore.Runtime.Execution.Memory;
using RDCore.Runtime.StdLib;

namespace RDCore.Runtime.Compiled;

// what compiled code does to a String variable of the activation besides assigning it a value: the Mid statement.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// The <c>Mid</c> statement of a <c>String</c> variable held in <paramref name="cell"/>, in characters (MS-VBAL §5.4.3.5): from
    /// <paramref name="start"/>, the next characters of the variable are replaced by the first characters of <paramref name="value"/>, as many as the least
    /// of the length, the characters left from <paramref name="start"/> and the characters of <paramref name="value"/>; the variable keeps its length.
    /// </summary>
    /// <param name="cell">The cell of the String variable.</param>
    /// <param name="start">The 1-based position of the first character replaced.</param>
    /// <param name="length">The most characters replaced, when <paramref name="hasLength"/>.</param>
    /// <param name="hasLength">Whether the statement gives a length.</param>
    /// <param name="value">The text the characters are taken from; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns>
    /// <see langword="false"/> when the interpreter runs the statement: a start below 1 or past the end, or a negative length, which is error 5; a cell that
    /// holds no text compiled code reads; or a String of the session in a <c>BSTR</c> (<c>StrPtr</c>), which the interpreter writes in place.
    /// </returns>
    public bool TryMid(object? cell, long start, long length, bool hasLength, string? value)
    {
        if (FailsAccessAtRandom() || _session.Storage is SessionStorage { HasStringBlocks: true } || !StringOperations.TryRead(cell, out var held))
        {
            return false;
        }

        var text = held ?? string.Empty;
        if (start > text.Length || length > int.MaxValue
            || StdStrings.MidRange(text.Length, (int)Math.Max(start, 0), hasLength ? (int)length : null) is not var (index, count))
        {
            return false;
        }

        var replaced = Math.Min(count, value?.Length ?? 0);
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
}
