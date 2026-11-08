using RDCore.SDK.Model;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Semantics;

/// <summary>
/// What is known of an operand of an operation read as a truth value: a constant and its value, or only what its type says it can hold.
/// </summary>
public enum TruthOperandKind
{
    /// <summary>
    /// Nothing that matters: an operand whose value is not known, of a type that holds numbers (a variable <c>As Long</c>). An operation of it works on bits
    /// on purpose, as a mask does.
    /// </summary>
    Opaque = 0,

    /// <summary>
    /// A constant number: its value is known (<see cref="TruthOperand.Bits"/>).
    /// </summary>
    Number,

    /// <summary>
    /// A <c>Boolean</c>: <c>True</c> (<c>-1</c>) or <c>False</c> (<c>0</c>), whose value is known when <see cref="TruthOperand.Bits"/> says it.
    /// </summary>
    Boolean,

    /// <summary>
    /// An operand whose value is not known and could be <c>True</c> or <c>False</c>: a <c>Variant</c>, or a conditional-compilation constant the module does not define.
    /// </summary>
    Unknown,

    /// <summary>
    /// A predefined conditional-compilation constant (<c>Win64</c>, <c>VBA7</c>, ...): <c>1</c> where it is true and <c>0</c> where it is not, as MS-VBA defines
    /// them, and which of the two depends on the environment.
    /// </summary>
    OneOrZero,
}

/// <summary>
/// An operand of an operation read as a truth value.
/// </summary>
/// <param name="Kind">What is known of it.</param>
/// <param name="Bits">Its value as the logical operators see it, when it is a constant: a number rounded to a <c>Long</c> integer as they round it, <c>-1</c> for
/// <c>True</c>; <see langword="null"/> when it is not known.</param>
public readonly record struct TruthOperand(TruthOperandKind Kind, long? Bits = null)
{
    /// <summary>
    /// An operand that does not matter (<see cref="TruthOperandKind.Opaque"/>).
    /// </summary>
    public static TruthOperand Opaque => new(TruthOperandKind.Opaque);

    /// <summary>
    /// An operand that could be <c>True</c> or <c>False</c>, and is not known to be either (<see cref="TruthOperandKind.Unknown"/>).
    /// </summary>
    public static TruthOperand Unknown => new(TruthOperandKind.Unknown);

    /// <summary>
    /// A <c>Boolean</c> whose value is not known.
    /// </summary>
    public static TruthOperand AnyBoolean => new(TruthOperandKind.Boolean);

    /// <summary>
    /// A predefined conditional-compilation constant that can be true: <c>1</c> or <c>0</c> (<see cref="TruthOperandKind.OneOrZero"/>).
    /// </summary>
    public static TruthOperand OneOrZero => new(TruthOperandKind.OneOrZero);

    /// <summary>
    /// A constant, from its value: a number or a <c>Boolean</c>, and <see cref="Opaque"/> for any other value (a <c>String</c>, a <c>Date</c>, <c>Null</c>).
    /// </summary>
    /// <param name="value">The value of the constant.</param>
    public static TruthOperand Of(VBTypedValue value) => value switch
    {
        VBBooleanValue boolean => new(TruthOperandKind.Boolean, (bool)boolean.Value ? -1 : 0),
        VBDateValue => Opaque,
        VBNumericTypedValue number when Rounded(number.AsDouble) is { } bits => new(TruthOperandKind.Number, bits),
        _ => Opaque,
    };

    /// <summary>
    /// A constant number that is not a <c>Boolean</c>'s value: not <c>0</c>, and not <c>-1</c>, or a predefined constant that is <c>1</c> where it is true. It
    /// is <c>True</c> where a condition tests it, and still not <c>True</c> where its bits are what an operator works on.
    /// </summary>
    public bool IsTrueOtherThanMinusOne
        => Kind is TruthOperandKind.OneOrZero || (Kind is TruthOperandKind.Number && Bits is { } bits && bits is not 0 and not -1);

    /// <summary>
    /// The operand negated, as a unary <c>-</c> before a constant makes it.
    /// </summary>
    public TruthOperand Negated => Kind is TruthOperandKind.Number && Bits is { } bits ? this with { Bits = -bits } : Opaque;

    // MS-VBAL §5.6.9.8: a logical operator Let-coerces a number to Long (or LongLong), rounding as Let-coercion does.
    private static long? Rounded(double number)
        => double.IsFinite(number) && Math.Abs(number) < 9.2e18 ? (long)Math.Round(number, MidpointRounding.ToEven) : null;
}

/// <summary>
/// Whether an operation that reads a constant as a truth value gives a different answer than its operands' truth values would: the condition a
/// <c>True</c> that is not <c>-1</c> makes wrong.
/// </summary>
/// <remarks>
/// <para>
/// A condition is Let-coerced to <c>Boolean</c>, which makes any value that is not zero <c>True</c> (<strong>MS-VBAL §5.4.2.8</strong>, <strong>§3.4.2</strong>,
/// <strong>§5.5.1.2.2</strong>), so <c>1</c> is as true as <c>True</c> where a condition tests it. The
/// logical operators work on bits, though (<strong>MS-VBAL §5.6.9.8</strong>): <c>Not 1</c> is <c>-2</c>, which is true, <c>1 And 2</c> is <c>0</c>, which is
/// false, and <c>1 = True</c> is <c>False</c>. <c>True</c> is <c>-1</c>, every bit set, which is what makes them right for a <c>Boolean</c>; a constant that
/// stands for a truth value with another number makes them wrong.
/// </para>
/// <para>
/// An operation is reported only when that can change its answer: when every operand is known, when its answer differs; when one is a <c>Boolean</c> or could
/// be one, when it differs for <c>True</c> or for <c>False</c>. <c>Or</c> never does: a number that is not zero keeps a bit set whatever it is combined with.
/// An operand of a type that holds numbers, whose value is not known, makes the operation one on bits, which is the bitwise-operator hint's to say.
/// </para>
/// </remarks>
public static class TruthValueOperations
{
    /// <summary>
    /// Whether <c>Not</c> of <paramref name="operand"/> is not the negation of its truth value: <c>Not</c> of a constant that is <c>True</c> and not <c>-1</c>
    /// is still <c>True</c>.
    /// </summary>
    /// <param name="operand">The operand of <c>Not</c>.</param>
    public static bool NotMisleads(TruthOperand operand) => operand.IsTrueOtherThanMinusOne;

    /// <summary>
    /// Whether the binary operation <paramref name="token"/> of <paramref name="left"/> and <paramref name="right"/> can give a different truth value than the
    /// truth values of its operands give, because one of them is a constant that is <c>True</c> and not <c>-1</c>.
    /// </summary>
    /// <param name="token">The operator (<see cref="Tokens.LogicalAndOp"/>, <see cref="Tokens.LogicalOrOp"/>, <see cref="Tokens.LogicalXOrOp"/>,
    /// <see cref="Tokens.LogicalEqvOp"/>, <see cref="Tokens.LogicalImpOp"/>, <see cref="Tokens.CompareEqualOp"/>, <see cref="Tokens.CompareNotEqualOp"/>); any other
    /// is never one.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <remarks>
    /// A comparison is one only of a constant number with a <c>Boolean</c> or with <c>-1</c>: <c>DEV = 1</c> compares numbers, as it says, and
    /// <c>DEV = True</c> (or <c>DEV = -1</c>, <c>True</c>'s value) asks whether <c>DEV</c> is true, which <c>1 = True</c> answers <c>False</c>.
    /// </remarks>
    public static bool Misleads(string token, TruthOperand left, TruthOperand right)
    {
        if (!left.IsTrueOtherThanMinusOne && !right.IsTrueOtherThanMinusOne)
        {
            return false;
        }

        Func<long, long, (bool Bitwise, bool Logical)>? operation = token switch
        {
            Tokens.LogicalAndOp => (a, b) => ((a & b) != 0, a != 0 && b != 0),
            Tokens.LogicalOrOp => (a, b) => ((a | b) != 0, a != 0 || b != 0),
            Tokens.LogicalXOrOp => (a, b) => ((a ^ b) != 0, (a != 0) != (b != 0)),
            Tokens.LogicalEqvOp => (a, b) => (~(a ^ b) != 0, (a != 0) == (b != 0)),
            Tokens.LogicalImpOp => (a, b) => ((~a | b) != 0, a == 0 || b != 0),
            Tokens.CompareEqualOp when IsComparedWithABoolean(left, right) => (a, b) => (a == b, (a != 0) == (b != 0)),
            Tokens.CompareNotEqualOp when IsComparedWithABoolean(left, right) => (a, b) => (a != b, (a != 0) != (b != 0)),
            _ => null,
        };

        if (operation is null)
        {
            return false;
        }

        foreach (var a in ValuesOf(left))
        {
            foreach (var b in ValuesOf(right))
            {
                var (bitwise, logical) = operation(a, b);
                if (bitwise != logical)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsComparedWithABoolean(TruthOperand left, TruthOperand right)
        => (left.IsTrueOtherThanMinusOne && IsABooleansValue(right)) || (right.IsTrueOtherThanMinusOne && IsABooleansValue(left));

    private static bool IsABooleansValue(TruthOperand operand) => operand.Kind is TruthOperandKind.Boolean || operand is { Kind: TruthOperandKind.Number, Bits: -1 };

    // what an operand can be, as far as the question goes: its value when it is known, True and False when it could be either, and nothing at all when it
    // holds numbers nobody knows.
    private static IEnumerable<long> ValuesOf(TruthOperand operand) => operand switch
    {
        { Bits: { } bits } => [bits],
        { Kind: TruthOperandKind.Boolean or TruthOperandKind.Unknown } => [-1, 0],
        { Kind: TruthOperandKind.OneOrZero } => [1, 0],
        _ => [],
    };
}
