using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Numerics;

namespace RDCore.Runtime.Semantics.Operators;

/// <summary>
/// The value of a <c>Single</c>, <c>Double</c> or <c>Date</c> operation as MS-VBA computes it, in the 80-bit extended format, before it is stored: its type
/// is the effective type of the operation, and the exceptions its operations signalled are pending until it is (<see cref="ExtendedIntermediates"/>).
/// </summary>
/// <param name="Value">The value, rounded to 64 bits.</param>
/// <param name="Type">The type of the expression: <c>Single</c>, <c>Double</c> or <c>Date</c>.</param>
/// <param name="Pending">The exceptions the operations that computed it signalled.</param>
internal readonly record struct WideValue(Float80 Value, VBType Type, FloatingPointExceptions Pending);

/// <summary>
/// <strong>MS-VBAL 5.6.9.3</strong>: MS-VBA performs the floating-point operations of an expression "with a higher-precision than the effective value type",
/// as the specification allows: an x87's, of 80 bits. An expression of operands of declared types whose operation's effective type is <c>Single</c>,
/// <c>Double</c> or <c>Date</c> is computed in that format, operation after operation, and rounded where the value is stored (recorded in Excel, grid
/// <c>oracle\s42</c>).
/// </summary>
/// <remarks>
/// <para>
/// Where the value goes decides what it is rounded to. Let-coerced to a declared numeric type - a variable assigned, a parameter, a function's result, the
/// operand of an operator of another effective type, the bound of a <c>For</c> - it is converted to that type from the 80 bits: <c>e = s + 1</c> with
/// <c>s</c> a <c>Single</c> of 16777216 and <c>e</c> a <c>Double</c> assigns 16777217, and <c>l = 2.5 + 2^-60</c> assigns 3 to a <c>Long</c> (s42 B07, C14,
/// D01-D31). Held as itself - by a <c>Variant</c>, written as a <c>String</c>, as the selector of a <c>Select Case</c> - it is stored as the type of the
/// expression first (D03, D18, D22, D25). Compared, it is rounded to the precision of the comparison's effective type, in the extended range: <c>d * 10 >
/// 0</c> is <c>True</c> for a <c>Double</c> of 1E+308, and <c>1 + 2^-60 > 1</c> is <c>False</c> (A22, B18, B19).
/// </para>
/// <para>
/// The exceptions are the x87's, kept until the value is stored, and evaluation goes on: <c>(1 / 0) - (1 / 0)</c> assigns a NaN and raises 11 (A39). A
/// division by zero is error 11, an invalid operation or an overflow error 6 - a division by zero first (C07). A value past the range of a <c>Double</c>
/// overflows only where it is stored as one (A01-A13). A <c>Single</c> or <c>Double</c> variable is assigned the value it is rounded to before the error is
/// raised, as before; any other destination is left as it was.
/// </para>
/// <para>
/// An operation with a <c>Variant</c> operand is MS-VBA's Variant arithmetic, which rounds each result to its type (C10-C13): none of this applies to it.
/// </para>
/// </remarks>
internal static class ExtendedIntermediates
{
    /// <summary>Whether an operation of this effective type is computed in the extended format.</summary>
    public static bool IsFloating(VBType? effectiveType) => effectiveType is VBSingleType or VBDoubleType or VBDateType;

    /// <summary>Whether a let-coercion to <paramref name="destination"/> converts the extended value itself, rather than the value stored as its type.</summary>
    public static bool ConvertsDirectly(VBType destination)
        => destination is VBByteType or VBIntegerType or VBLongType or VBLongLongType or VBCurrencyType or VBSingleType or VBDoubleType or VBBooleanType
            or VBDateType;

    /// <summary>Whether <paramref name="token"/> is one of the four operators computed in the extended format.</summary>
    public static bool IsArithmetic(string token) => token is Tokens.AdditionOp or Tokens.SubtractionOp or Tokens.MultiplicationOp or Tokens.DivisionOp;

    /// <summary>Whether <paramref name="token"/> is a comparison of numbers.</summary>
    public static bool IsComparison(string token)
        => token is Tokens.CompareEqualOp or Tokens.CompareNotEqualOp or Tokens.CompareLessThanOp or Tokens.CompareLessThanOrEqualOp
            or Tokens.CompareGreaterThanOp or Tokens.CompareGreaterThanOrEqualOp;

    /// <summary>The value of a payload, exactly: a <c>Currency</c> is its ten-thousandths divided by 10,000, rounded once (s42 C23, C24).</summary>
    public static Float80 Of(ScalarPayload payload) => payload.Type switch
    {
        VBCurrencyType => Float80.FromCurrency(payload.Integral),
        _ when payload.IsIntegral => Float80.FromInt64(payload.Integral),
        _ => Float80.FromDouble(payload.Floating),
    };

    /// <summary>A payload of an operation's effective type, as an extended value that has signalled nothing.</summary>
    public static WideValue Widened(ScalarPayload payload, VBType type) => new(Of(payload), type, FloatingPointExceptions.None);

    /// <summary>
    /// One of the four operations, of two extended values: its value has the effective type, and the exceptions of both operands and its own pending.
    /// </summary>
    public static WideValue Operate(string token, WideValue left, WideValue right, VBType effectiveType)
    {
        FloatingPointExceptions raised;
        var value = token switch
        {
            Tokens.AdditionOp => Float80.Add(left.Value, right.Value, out raised),
            Tokens.SubtractionOp => Float80.Subtract(left.Value, right.Value, out raised),
            Tokens.MultiplicationOp => Float80.Multiply(left.Value, right.Value, out raised),
            Tokens.DivisionOp => Float80.Divide(left.Value, right.Value, out raised),
            _ => throw new ArgumentOutOfRangeException(nameof(token)),
        };

        return new WideValue(value, effectiveType, left.Pending | right.Pending | raised);
    }

    /// <summary>The negation of an extended value, which signals nothing.</summary>
    public static WideValue Negate(WideValue operand, VBType effectiveType) => operand with { Value = operand.Value.Negate(), Type = effectiveType };

    /// <summary>
    /// The error a value with these exceptions pending raises where it is stored: a division by zero is error 11; an invalid operation or an overflow,
    /// error 6.
    /// </summary>
    public static VBRuntimeErrorId? ErrorOf(FloatingPointExceptions exceptions)
        => (exceptions & FloatingPointExceptions.DivideByZero) != 0 ? VBRuntimeErrorId.DivisionByZero
            : exceptions != FloatingPointExceptions.None ? VBRuntimeErrorId.Overflow
            : null;

    /// <summary>
    /// An extended value converted to <paramref name="destination"/>, one of the types it converts to directly (<see cref="ConvertsDirectly"/>), and the
    /// error the conversion raises. A <c>Single</c> and a <c>Double</c> are what the value rounds to even when it raises - an infinity, a NaN - which a
    /// variable of that type is assigned before the error is raised; no other destination has a value when it raises.
    /// </summary>
    /// <param name="value">The extended value.</param>
    /// <param name="destination">The type it is converted to.</param>
    /// <param name="error">The error the conversion raises, or <see langword="null"/>.</param>
    /// <returns>The payload it converts to; <see langword="null"/> when it raises and has no value.</returns>
    public static ScalarPayload? Convert(WideValue value, VBType destination, out VBRuntimeErrorId? error)
    {
        error = ErrorOf(value.Pending);
        switch (destination)
        {
            case VBDoubleType:
            {
                var stored = value.Value.ToDouble(out var raised);
                error ??= ErrorOf(raised);
                return new ScalarPayload(destination, 0, stored);
            }
            case VBSingleType:
            {
                var stored = value.Value.ToSingle(out var raised);
                error ??= ErrorOf(raised);
                return new ScalarPayload(destination, 0, stored);
            }
            case VBDateType:
            {
                var serial = value.Value.ToDouble(out var raised);
                error ??= ErrorOf(raised);
                if (error is null && !VBDateType.IsValidSerial(serial))
                {
                    error = VBRuntimeErrorId.Overflow;
                }

                return error is null ? new ScalarPayload(destination, 0, serial) : null;
            }
        }

        if (error is not null)
        {
            return null;
        }

        // MS-VBAL 5.5.1.2.1: a number let-coerced to an integer type is rounded to the nearest, ties to even - and so is a Currency's ten-thousandths. What
        // is no number, or past the range of the type, is an overflow.
        if (destination is VBBooleanType)
        {
            if (value.Value.IsNaN)
            {
                error = VBRuntimeErrorId.Overflow;
                return null;
            }

            return ScalarPayload.OfBoolean(!value.Value.IsZero);
        }

        var scaled = destination is VBCurrencyType ? Float80.Multiply(value.Value, Float80.FromInt64(ScalarPayload.CurrencyScale), out _) : value.Value;
        if (scaled.RoundToIntegral(MidpointRounding.ToEven).TryToInt64(out var integral))
        {
            if (destination is VBCurrencyType)
            {
                return ScalarPayload.OfCurrency(integral);
            }

            if (ScalarPayload.TryIntegral(destination, integral, out var payload))
            {
                return payload;
            }
        }

        error = VBRuntimeErrorId.Overflow;
        return null;
    }

    /// <summary>
    /// An extended value stored as the type of its expression: what a <c>Variant</c> holds of it, what is written of it, what anything that does not
    /// let-coerce it to a numeric type is given.
    /// </summary>
    public static ScalarPayload? Store(WideValue value, out VBRuntimeErrorId? error) => Convert(value, value.Type, out error);

    /// <summary>
    /// A comparison of two extended values of the comparison's effective type: each is rounded to that type's precision - not its range - and a NaN,
    /// ordered with nothing, is an invalid operation (s42 A22, B18, B19, C08, C09, D05).
    /// </summary>
    /// <returns>The result of the comparison; <see langword="null"/> when it raises <paramref name="error"/>.</returns>
    public static bool? Compare(string token, WideValue left, WideValue right, VBType effectiveType, out VBRuntimeErrorId? error)
    {
        var l = effectiveType is VBSingleType ? left.Value.RoundToSinglePrecision() : left.Value.RoundToDoublePrecision();
        var r = effectiveType is VBSingleType ? right.Value.RoundToSinglePrecision() : right.Value.RoundToDoublePrecision();
        var order = Float80.Compare(l, r);
        error = ErrorOf(left.Pending | right.Pending | (order is null ? FloatingPointExceptions.Invalid : FloatingPointExceptions.None));
        if (error is not null || order is not { } sign)
        {
            return null;
        }

        return token switch
        {
            Tokens.CompareEqualOp => sign == 0,
            Tokens.CompareNotEqualOp => sign != 0,
            Tokens.CompareLessThanOp => sign < 0,
            Tokens.CompareLessThanOrEqualOp => sign <= 0,
            Tokens.CompareGreaterThanOp => sign > 0,
            Tokens.CompareGreaterThanOrEqualOp => sign >= 0,
            _ => throw new ArgumentOutOfRangeException(nameof(token)),
        };
    }

    /// <summary>
    /// What the standard library's <c>Abs</c>, <c>Int</c>, <c>Fix</c>, <c>CSng</c> and <c>CDbl</c> make of an extended value, which MS-VBA computes without
    /// storing it: the first three keep its type and its 64 bits (s42 A10-A13, C16-C18, D10, D11); <c>CDbl</c> rounds it to a <c>Double</c>, and
    /// <c>CSng</c> to a <c>Double</c> and then to the precision of a <c>Single</c>, in the range of a <c>Double</c> - a <c>Double</c> past its range is an
    /// infinity and an overflow, pending (A16, A17, C02-C06).
    /// </summary>
    /// <returns>The value; <see langword="null"/> when <paramref name="function"/> is none of them.</returns>
    public static WideValue? Apply(string function, WideValue argument) => !Applies(function) ? null : function switch
    {
        "IStdMathModule.Abs/1" => argument with { Value = argument.Value.Abs() },
        "IStdConversionModule.Int/1" => argument with { Value = argument.Value.RoundToIntegral(MidpointRounding.ToNegativeInfinity) },
        "IStdConversionModule.Fix/1" => argument with { Value = argument.Value.RoundToIntegral(MidpointRounding.ToZero) },
        "IStdConversionModule.CDbl/1" => Rounded(argument, VBDoubleType.TypeInfo),
        _ => Rounded(argument, VBSingleType.TypeInfo),
    };

    /// <summary>Whether <paramref name="function"/> is one of the functions <see cref="Apply"/> computes.</summary>
    public static bool Applies(string function)
        => function is "IStdMathModule.Abs/1" or "IStdConversionModule.Int/1" or "IStdConversionModule.Fix/1" or "IStdConversionModule.CDbl/1"
            or "IStdConversionModule.CSng/1";

    private static WideValue Rounded(WideValue argument, VBType type)
    {
        var stored = Float80.FromDouble(argument.Value.ToDouble(out var raised));
        return new WideValue(type is VBSingleType ? stored.RoundToSinglePrecision() : stored, type, argument.Pending | raised);
    }

    /// <summary>
    /// The type the standard library's conversion functions of a numeric type convert an extended value to, directly: <c>CByte</c>, <c>CInt</c>,
    /// <c>CLng</c>, <c>CLngLng</c>, <c>CCur</c>, <c>CBool</c> (s42 C15, D12).
    /// </summary>
    /// <returns>The type; <see langword="null"/> when <paramref name="function"/> is none of them.</returns>
    public static VBType? ConversionOf(string function) => function switch
    {
        "IStdConversionModule.CByte/1" => VBByteType.TypeInfo,
        "IStdConversionModule.CInt/1" => VBIntegerType.TypeInfo,
        "IStdConversionModule.CLng/1" => VBLongType.TypeInfo,
        "IStdConversionModule.CLngLng/1" => VBLongLongType.TypeInfo,
        "IStdConversionModule.CCur/1" => VBCurrencyType.TypeInfo,
        "IStdConversionModule.CBool/1" => VBBooleanType.TypeInfo,
        _ => null,
    };

    /// <summary>
    /// <c>Sgn</c> of an extended value: -1, 0 or 1, an <c>Integer</c>; the value is not stored, but what is pending is raised, and a NaN is an invalid
    /// operation (s42 A11, C45; oracle\s16 G10).
    /// </summary>
    public static ScalarPayload? SignOf(WideValue argument, out VBRuntimeErrorId? error)
    {
        error = ErrorOf(argument.Pending | (argument.Value.IsNaN ? FloatingPointExceptions.Invalid : FloatingPointExceptions.None));
        if (error is not null)
        {
            return null;
        }

        var sign = argument.Value.IsZero ? 0 : argument.Value.IsNegative ? -1 : 1;
        return new ScalarPayload(VBIntegerType.TypeInfo, sign, 0);
    }
}
