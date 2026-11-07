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
}
