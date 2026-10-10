using RDCore.External.Automation;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RDCore.Runtime.Execution.External.Automation;

/// <summary>
/// Turns the values of the language into the values an automation server takes, and what it gives back into the values of the language.
/// </summary>
/// <remarks>
/// <para>
/// The language's types and a server's do not correspond one to one, and the declared type of a member is what settles the ambiguities: a server's
/// <c>VT_CY</c> and <c>VT_DECIMAL</c> both arrive as a <see cref="decimal"/>, and a <c>Date</c> and a <c>Double</c> are both a <see cref="double"/> to the
/// language. A result is made the type the member was declared to return; only a <c>Variant</c> is left to what the server sent.
/// </para>
/// <para>
/// A value that cannot cross - a user-defined type, an object that is not a server's - is reported as the failure VBA reports it as, a type mismatch,
/// rather than sent as something else.
/// </para>
/// </remarks>
internal static class AutomationMarshaller
{
    private const int TypeMismatch = unchecked((int)0x80020005);
    private const int Overflow = unchecked((int)0x8002000A);

    // a Currency has four decimal places and a Decimal up to twenty-eight, and both arrive as a decimal: a value with no more than four is
    // what a Currency is, and one that has more cannot have been one.
    private const int CurrencyScale = 4;

    /// <summary>
    /// Makes <paramref name="value"/> what a server is given.
    /// </summary>
    /// <param name="value">The value of the language.</param>
    /// <param name="handleOf">Gets the handle behind an object the server owns, or <see langword="null"/> for an object that is not one.</param>
    /// <param name="omitEmpty">Whether an <c>Empty</c> is an argument that was left out, which the server is told by an absent one (an optional <c>Variant</c>).</param>
    /// <exception cref="AutomationException">The value cannot be given to a server.</exception>
    public static object? ToAutomation(VBTypedValue value, Func<VBRuntimeObjectId, object?> handleOf, bool omitEmpty = false)
    {
        switch (value)
        {
            case VBVariantValue variant:
                return ToAutomation(variant.TypedValue, handleOf, omitEmpty);
            case VBEmptyValue:
                return omitEmpty ? Type.Missing : null;
            case VBMissingValue:
                return Type.Missing;
            case VBNullValue:
                return DBNull.Value;
            case VBBooleanValue boolean:
                return (bool)boolean.Value;
            case VBByteValue @byte:
                return @byte.Value;
            case VBIntegerValue integer:
                return integer.Value;
            case VBLongValue @long:
                return @long.Value;
            case VBLongLongValue longLong:
                return longLong.Value;
            case VBSingleValue single:
                return single.Value;
            case VBDoubleValue @double:
                return @double.Value;
            case VBCurrencyValue currency:
                return new CurrencyWrapper(currency.Value.Value);
            case VBDecimalValue @decimal:
                return @decimal.Value;
            case VBDateValue date:
                return date.Value;
            case VBStringValue text:
                return text.Value ?? string.Empty;
            case VBErrorValue error:
                return new ErrorWrapper(error.Value);
            case VBObjectValue { } reference:
                return reference.IsNothing()
                    ? new DispatchWrapper(null)
                    : handleOf(reference.Value) ?? throw new AutomationException(TypeMismatch, "An object that is not a server's cannot be given to one.");
            case VBArrayValue array:
                return ToAutomation(array, handleOf);
            default:
                throw new AutomationException(TypeMismatch, $"A value of type '{value.TypeInfo.Name}' cannot be given to a server.");
        }
    }

    // a SAFEARRAY has the bounds the array has, and the element type its elements are declared: a Variant array is an array of objects.
    private static Array ToAutomation(VBArrayValue array, Func<VBRuntimeObjectId, object?> handleOf)
    {
        var lengths = array.Dimensions.Select(dimension => dimension.Length).ToArray();
        var lowerBounds = array.Dimensions.Select(dimension => dimension.LowerBound).ToArray();
        var result = Array.CreateInstance(ElementType(array.ItemType), lengths, lowerBounds);

        foreach (var subscripts in Subscripts(array))
        {
            result.SetValue(
                ToAutomation(array[subscripts] ?? throw new AutomationException(TypeMismatch, "An element of an array cannot be read."), handleOf),
                subscripts);
        }

        return result;
    }

    private static Type ElementType(VBType itemType) => itemType switch
    {
        VBByteType => typeof(byte),
        VBIntegerType => typeof(short),
        VBLongType => typeof(int),
        VBLongLongType => typeof(long),
        VBSingleType => typeof(float),
        VBDoubleType => typeof(double),
        VBBooleanType => typeof(bool),
        VBStringType => typeof(string),
        VBDateType => typeof(DateTime),
        _ => typeof(object),
    };

    // the subscripts of every element of an array, the first varying fastest: the order a SAFEARRAY is laid out in.
    private static IEnumerable<int[]> Subscripts(VBArrayValue array)
    {
        if (!array.IsInitialized || array.Length == 0)
        {
            yield break;
        }

        var current = array.Dimensions.Select(dimension => dimension.LowerBound).ToArray();
        while (true)
        {
            yield return [.. current];

            var dimension = 0;
            while (dimension < current.Length && ++current[dimension] > array.Dimensions[dimension].UpperBound)
            {
                current[dimension] = array.Dimensions[dimension].LowerBound;
                dimension++;
            }

            if (dimension == current.Length)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Makes what a server returned the value of the type the member was declared to return.
    /// </summary>
    /// <param name="value">What the server returned.</param>
    /// <param name="declared">The declared type of the member.</param>
    /// <param name="wrap">Makes the object the language holds for an object the server returned, of the class that it is declared to be.</param>
    /// <exception cref="AutomationException">The value is not one of the declared type.</exception>
    public static VBTypedValue FromAutomation(object? value, VBType declared, Func<object, VBType, VBTypedValue> wrap)
    {
        try
        {
            return declared switch
            {
                VBVoidType => VBVoidValue.Void,
                VBVariantType => new VBVariantValue(Natural(value, wrap)),
                VBBooleanType => new VBBooleanValue(Convert.ToBoolean(Present(value), CultureInfo.InvariantCulture)),
                VBByteType => new VBByteValue(Convert.ToByte(Present(value), CultureInfo.InvariantCulture)),
                VBIntegerType => new VBIntegerValue(Convert.ToInt16(Present(value), CultureInfo.InvariantCulture)),
                VBLongType or VBEnumType => new VBLongValue(Convert.ToInt32(Present(value), CultureInfo.InvariantCulture)),
                VBLongLongType => new VBLongLongValue(Convert.ToInt64(Present(value), CultureInfo.InvariantCulture)),
                VBSingleType => new VBSingleValue(Convert.ToSingle(Present(value), CultureInfo.InvariantCulture)),
                VBDoubleType => new VBDoubleValue(Convert.ToDouble(Present(value), CultureInfo.InvariantCulture)),
                VBCurrencyType => new VBCurrencyValue(Convert.ToDecimal(Present(value), CultureInfo.InvariantCulture)),
                VBDecimalType => new VBDecimalValue(Convert.ToDecimal(Present(value), CultureInfo.InvariantCulture)),
                VBDateType => new VBDateValue(Convert.ToDateTime(Present(value), CultureInfo.InvariantCulture).ToOADate()),
                VBStringType => new VBStringValue(Convert.ToString(Present(value), CultureInfo.InvariantCulture) ?? string.Empty),
                VBClassType or VBObjectType or VBUnknownType => value is null or DBNull ? VBObjectValue.Nothing : wrap(value, declared),
                VBArrayType => Natural(value, wrap),
                _ => throw new AutomationException(TypeMismatch, $"A value of type '{declared.Name}' cannot be taken from a server."),
            };
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException)
        {
            throw new AutomationException(TypeMismatch, $"A value the server returned is not a '{declared.Name}'.");
        }
        catch (OverflowException)
        {
            throw new AutomationException(Overflow, $"A value the server returned does not fit in a '{declared.Name}'.");
        }
    }

    // a number or a string where the server said nothing is the type's own default, as an uninitialized variable is.
    private static object Present(object? value) => value is null or DBNull or Missing ? 0 : value;

    /// <summary>
    /// Makes what a server returned the value of the type it is, for a member that is declared to return a <c>Variant</c>.
    /// </summary>
    private static VBTypedValue Natural(object? value, Func<object, VBType, VBTypedValue> wrap) => value switch
    {
        null or Missing => VBEmptyValue.Empty,
        DBNull => VBNullValue.Null,
        bool boolean => new VBBooleanValue(boolean),
        byte @byte => new VBByteValue(@byte),
        short integer => new VBIntegerValue(integer),
        int @long => new VBLongValue(@long),
        long longLong => new VBLongLongValue(longLong),
        float single => new VBSingleValue(single),
        double @double => new VBDoubleValue(@double),
        decimal @decimal => @decimal == decimal.Round(@decimal, CurrencyScale) ? new VBCurrencyValue(@decimal) : new VBDecimalValue(@decimal),
        string text => new VBStringValue(text),
        DateTime date => new VBDateValue(date.ToOADate()),
        ErrorWrapper error => new VBErrorValue(error.ErrorCode),
        Array array => FromAutomation(array, wrap),
        _ => wrap(value, VBObjectType.TypeInfo),
    };

    private static VBArrayValue FromAutomation(Array array, Func<object, VBType, VBTypedValue> wrap)
    {
        var bounds = Enumerable.Range(0, array.Rank).Select(dimension => (array.GetLowerBound(dimension), array.GetUpperBound(dimension))).ToArray();
        var result = new VBResizableArrayValue(bounds, VBVariantType.TypeInfo);

        foreach (var subscripts in Subscripts(result))
        {
            var element = new VBVariantValue(Natural(array.GetValue(subscripts), wrap));
            _ = result.TrySetElement(new ValueBindingHandle(element.RuntimeValue), subscripts);
        }

        return result;
    }
}
