using RDCore.SDK.Model;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Diagnostics.Analyzers;

// what is known of an operand of an operation read as a truth value: a constant and its value, or only what its type says it can hold.
internal enum TruthOperandKind
{
    // an operand whose value is not known, of a type that holds numbers (a variable As Long): an operation of it works on bits on purpose, as a mask does.
    Opaque = 0,

    // a constant number, whose value is known.
    Number,

    // True (-1) or False (0), whose value is known when the bits say it.
    Boolean,

    // an operand whose value is not known and could be True or False: a Variant, or a conditional-compilation constant the module does not define.
    Unknown,

    // a predefined conditional-compilation constant (Win64, VBA7, ...): 1 where it is true and 0 where it is not, as MS-VBA defines them, and which of the two
    // depends on the environment.
    OneOrZero,
}

// an operand of an operation read as a truth value; its bits are its value as the logical operators see it when it is a constant: a number rounded to a Long
// integer as they round it, -1 for True.
internal readonly record struct TruthOperand(TruthOperandKind Kind, long? Bits = null)
{
    public static TruthOperand Opaque => new(TruthOperandKind.Opaque);

    public static TruthOperand Unknown => new(TruthOperandKind.Unknown);

    public static TruthOperand AnyBoolean => new(TruthOperandKind.Boolean);

    public static TruthOperand OneOrZero => new(TruthOperandKind.OneOrZero);

    // a constant, from its value: a number or a Boolean, and opaque for any other value (a String, a Date, Null).
    public static TruthOperand Of(VBTypedValue value) => value switch
    {
        VBBooleanValue boolean => new(TruthOperandKind.Boolean, (bool)boolean.Value ? -1 : 0),
        VBDateValue => Opaque,
        VBNumericTypedValue number when Rounded(number.AsDouble) is { } bits => new(TruthOperandKind.Number, bits),
        _ => Opaque,
    };

    // a constant number that is not a Boolean's value (not 0, not -1), or a predefined constant that is 1 where it is true: True where a condition tests it,
    // and still not True where its bits are what an operator works on.
    public bool IsTrueOtherThanMinusOne
        => Kind is TruthOperandKind.OneOrZero || (Kind is TruthOperandKind.Number && Bits is { } bits && bits is not 0 and not -1);

    // the operand negated, as a unary - before a constant makes it.
    public TruthOperand Negated => Kind is TruthOperandKind.Number && Bits is { } bits ? this with { Bits = -bits } : Opaque;

    // MS-VBAL §5.6.9.8: a logical operator Let-coerces a number to Long (or LongLong), rounding as Let-coercion does.
    private static long? Rounded(double number)
        => double.IsFinite(number) && Math.Abs(number) < 9.2e18 ? (long)Math.Round(number, MidpointRounding.ToEven) : null;
}

// MS-VBAL §5.4.2.8, §3.4.2, §5.5.1.2.2: a condition is Let-coerced to Boolean, which makes any value that is not zero True, so 1 is as true as True where a
// condition tests it. MS-VBAL §5.6.9.8: the logical operators work on bits, though: Not 1 is -2, which is true, 1 And 2 is 0, which is false, and 1 = True is
// False. True is -1, every bit set, which is what makes them right for a Boolean; a constant that stands for a truth value with another number makes them
// wrong. An operation is one only when that can change its answer: when every operand is known, when its answer differs; when one is a Boolean or could be
// one, when it differs for True or for False. Or never does: a number that is not zero keeps a bit set whatever it is combined with. An operand of a type
// that holds numbers, whose value is not known, makes the operation one on bits, which is the bitwise-operator hint's to say.
internal static class TruthValueOperations
{
    // Not of a constant that is True and not -1 is still True.
    public static bool NotMisleads(TruthOperand operand) => operand.IsTrueOtherThanMinusOne;

    // whether the binary operation can give a different truth value than the truth values of its operands give. A comparison is one only of a constant number
    // with a Boolean or with -1: DEV = 1 compares numbers, as it says, and DEV = True (or DEV = -1, True's value) asks whether DEV is true, which 1 = True
    // answers False.
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
