using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Semantics.Abstract;

/// <summary>
/// Recognizes a <c>Null</c> operand by its value, whether it is the <c>Null</c> literal or a <c>Variant</c> that holds <c>Null</c>.
/// </summary>
/// <remarks>
/// The operators' semantics speak of the operand's <em>value</em>: <strong>MS-VBAL §5.6.9.4</strong> Let-coerces only "its non-Null operands",
/// and the logical operators' tables (<strong>§5.6.9.8</strong>) have rows for a <c>Null</c> operand. A <c>Variant</c> holding <c>Null</c> is such an
/// operand as much as the literal is; testing for <see cref="VBNullValue"/> alone took it for a value to coerce, which raised error 94.
/// </remarks>
internal static class NullOperand
{
    /// <summary>
    /// Gets the <c>Null</c> that <paramref name="operand"/> is or holds.
    /// </summary>
    /// <param name="operand">An operand of an operator expression.</param>
    /// <param name="value">The <c>Null</c> value, unwrapped from the <c>Variant</c> that held it.</param>
    /// <returns><c>true</c> if the operand is <c>Null</c>; <c>false</c> for any other value, and for a <c>Variant</c> whose value is not known.</returns>
    public static bool TryGet(VBTypedValue operand, [NotNullWhen(true)] out VBNullValue? value)
    {
        value = operand switch
        {
            VBNullValue literal => literal,
            VBVariantValue { IsIndeterminate: false, TypedValue: VBNullValue held } => held,
            _ => null,
        };
        return value is not null;
    }

    /// <summary>
    /// Whether <paramref name="operand"/> is <c>Null</c> or a <c>Variant</c> that holds <c>Null</c>.
    /// </summary>
    public static bool Is(VBTypedValue operand) => TryGet(operand, out _);
}
