using System.Globalization;
using System.Text;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// What compiled code does of a <c>String</c> parameter or variable of variable length: it reads the text held in the cell its frame
/// holds it in (<see cref="CompiledActivation.StringCellOf"/>), joins texts, and writes the text a <c>Let</c> assigns, as the interpreter's evaluation of
/// Strings does (<c>RuntimeExpressionEvaluator.TryEvaluateText</c>). The interpreter reads the cells compiled code cannot read (a String that ends with a
/// lone byte, or one whose value is unknown): the operation returns <see langword="false"/>, and the interpreter runs the statement.
/// </summary>
public static class StringOperations
{
    /// <summary>
    /// The text held in <paramref name="cell"/>: <see langword="null"/> for <c>vbNullString</c>, which reads as the zero-length string.
    /// </summary>
    /// <param name="cell">The cell of the String variable.</param>
    /// <param name="text">The text read.</param>
    /// <returns>Whether the cell holds a text compiled code reads.</returns>
    public static bool TryRead(object? cell, out string? text)
    {
        if (cell is ValueBindingHandle { IsKnown: true, Value: VBRuntimeValue<string> held })
        {
            text = held.StoredValue;
            return true;
        }

        text = null;
        return false;
    }

    /// <summary>
    /// Two texts joined (MS-VBAL §5.6.9.4, §5.6.5): <c>vbNullString</c> joins as the zero-length string it reads as, and what is joined is never
    /// <c>vbNullString</c> (<c>VBStringValue.Concat</c>).
    /// </summary>
    /// <param name="left">The left text; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <param name="right">The right text; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns>The texts joined.</returns>
    public static string Concat(string? left, string? right) => string.Concat(left, right);

    /// <summary>
    /// A <c>Byte</c>, an <c>Integer</c>, a <c>Long</c> or a <c>LongLong</c> let-coerced to <c>String</c> (MS-VBAL §5.5.1.2.4): its digits, after a minus
    /// sign when it is negative, as the interpreter's let-coercion writes a whole number, whatever the regional settings.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The text of the number.</returns>
    public static string OfWhole(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Let-assigns <paramref name="text"/> to the String variable held in <paramref name="cell"/> (MS-VBAL §5.4.3.8): the variable holds the String it is
    /// assigned, as it is.
    /// </summary>
    /// <param name="cell">The cell of the String variable.</param>
    /// <param name="text">The text assigned; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns><see langword="false"/> when the frame holds the variable in no cell compiled code writes: the interpreter runs the statement.</returns>
    public static bool Write(object? cell, string? text)
    {
        if (cell is not ValueBindingHandle handle)
        {
            return false;
        }

        handle.Store(new VBRuntimeValue<string>(text!), isKnown: true);
        return true;
    }

    /// <summary>
    /// Appends <paramref name="text"/> to the String variable held in <paramref name="cell"/>, as <c>S = S &amp; text</c> assigns it (MS-VBAL §5.6.9.4,
    /// §5.4.3.8). The first append of the activation writes the joined String into the cell; the ones after it go to <paramref name="appender"/>, which
    /// holds the text until <see cref="WriteAppended"/> writes it into the cell, so that each append does not copy the whole String.
    /// </summary>
    /// <param name="appender">The activation's appender of the variable; <see langword="null"/> until its first append.</param>
    /// <param name="cell">The cell of the String variable.</param>
    /// <param name="text">The text appended; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns><see langword="false"/> when the cell holds no text compiled code reads: the interpreter runs the statement.</returns>
    public static bool TryAppend(ref TextAppender? appender, object? cell, string? text)
    {
        if (cell is not ValueBindingHandle handle)
        {
            return false;
        }

        if (appender is null)
        {
            if (!TryRead(handle, out var held))
            {
                return false;
            }

            handle.Store(new VBRuntimeValue<string>(string.Concat(held, text)), isKnown: true);
            appender = new TextAppender();
            return true;
        }

        return appender.TryAppend(handle, text);
    }

    /// <summary>
    /// Writes what <paramref name="appender"/> holds into the String variable held in <paramref name="cell"/>, before anything reads the cell: compiled code,
    /// a callee the variable is passed to by reference, the interpreter, or a debugger.
    /// </summary>
    /// <param name="appender">The activation's appender of the variable; <see langword="null"/> before its first append.</param>
    /// <param name="cell">The cell of the String variable.</param>
    public static void WriteAppended(TextAppender? appender, object? cell)
    {
        if (appender is not null && cell is ValueBindingHandle handle)
        {
            appender.WriteInto(handle);
        }
    }
}

/// <summary>
/// The text compiled code appends to a <c>String</c> variable of an activation (<see cref="StringOperations.TryAppend"/>), held in a builder until it is
/// written into the variable's cell (<see cref="StringOperations.WriteAppended"/>). No String is changed in place: the cell is given a new String made of
/// the builder. A cell that holds a value other than the one the builder was made of was assigned by something else, and the builder is made again of it.
/// </summary>
public sealed class TextAppender
{
    private readonly StringBuilder _builder = new();

    // the value of the cell the builder was made of, or last written as; and whether the builder holds appends the cell does not have yet.
    private object? _held;
    private bool _behind;

    internal bool TryAppend(ValueBindingHandle handle, string? text)
    {
        var value = handle.Value;
        if (value is null || !ReferenceEquals(value, _held))
        {
            if (!StringOperations.TryRead(handle, out var held))
            {
                _held = null;
                _behind = false;
                return false;
            }

            _ = _builder.Clear().Append(held);
            _held = value;
        }

        _ = _builder.Append(text);
        _behind = true;
        return true;
    }

    internal void WriteInto(ValueBindingHandle handle)
    {
        if (!_behind)
        {
            return;
        }

        _behind = false;
        if (!ReferenceEquals(handle.Value, _held))
        {
            _held = null;
            return;
        }

        handle.Store(new VBRuntimeValue<string>(_builder.ToString()), isKnown: true);
        _held = handle.Value;
    }
}
