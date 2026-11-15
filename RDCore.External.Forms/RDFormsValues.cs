using RDCore.External.Automation;
using RDCore.Forms.Model;
using System.Globalization;
using System.Runtime.InteropServices;

namespace RDCore.External.Forms.RDForms;

// the values of RDForms' model, as the automation servers' neutral values are: what a property holds or an event is raised with, and what a program assigns.
internal static class RDFormsValues
{
    private const int TypeMismatch = unchecked((int)0x80020005);
    private const int Overflow = unchecked((int)0x8002000A);

    // VB error 380, which a control raises for a value its property cannot take.
    public const int InvalidPropertyValue = unchecked((int)0x800A017C);

    // VB error 94, Invalid use of Null: a Null where a number is needed.
    private const int InvalidUseOfNull = unchecked((int)0x800A005E);

    // an argument a program gives a method: a Single parameter takes it let-coerced to a Single (MS-VBAL §5.5.1.2), and a Variant one, or an argument past the
    // parameters, takes it as it is, for the method to check.
    public static FormValue ArgumentOf(object? value, MethodParameter? parameter, CultureInfo culture)
        => string.Equals(parameter?.TypeName, "Single", StringComparison.OrdinalIgnoreCase) ? FormValue.FromSingle(SingleOf(value, culture)) : VariantOf(value);

    public static float SingleOf(object? value, CultureInfo culture)
    {
        var number = Unwrapped(value) switch
        {
            null => 0d,
            DBNull => throw new AutomationException(InvalidUseOfNull, "Invalid use of Null"),
            bool truth => truth ? -1d : 0d,
            DateTime date => date.ToOADate(),
            var other => (double)FromNeutral(other, typeof(double), culture)!,
        };
        return Math.Abs(number) <= float.MaxValue ? (float)number : throw new AutomationException(Overflow, "Overflow");
    }

    private static FormValue VariantOf(object? value) => value switch
    {
        null => FormValue.Empty,
        DBNull => FormValue.Null,
        bool truth => FormValue.FromBoolean(truth),
        byte number => FormValue.FromByte(number),
        short number => FormValue.FromInteger(number),
        int number => FormValue.FromLong(number),
        long number => FormValue.FromLongLong(number),
        float number => FormValue.FromSingle(number),
        double number => FormValue.FromDouble(number),
        decimal number => FormValue.FromDecimal(number),
        DateTime date => FormValue.FromDate(date),
        string text => FormValue.FromString(text),
        ErrorWrapper error => FormValue.FromError(error.ErrorCode),
        RDFormsObject held => FormValue.FromObject(held.Component),
        _ when Unwrapped(value) is decimal currency => FormValue.FromCurrency(currency),
        _ => FormValue.FromObject(value),
    };

    // a property's value, as a server answers it: an enumeration is its number, and a colour its OLE_COLOR. A value of a type no program can hold yet is no value.
    public static bool TryToNeutral(object? value, out object? neutral)
    {
        switch (value)
        {
            case null:
                neutral = null;
                return true;
            case Enum constant:
                neutral = Convert.ChangeType(constant, Enum.GetUnderlyingType(constant.GetType()), CultureInfo.InvariantCulture);
                return true;
            case FormColor color:
                neutral = color.ToOle();
                return true;
            case string or bool or byte or short or int or long or float or double or decimal or DateTime:
                neutral = value;
                return true;
            default:
                neutral = null;
                return false;
        }
    }

    // what a program assigns to a property, as the property's type holds it.
    public static object? FromNeutral(object? value, Type type, CultureInfo culture)
    {
        try
        {
            if (type == typeof(string))
            {
                return Convert.ToString(value, culture) ?? string.Empty;
            }

            if (type.IsEnum)
            {
                var number = Convert.ChangeType(Unwrapped(value), Enum.GetUnderlyingType(type), culture);
                var constant = Enum.ToObject(type, number!);
                return Enum.IsDefined(type, constant) ? constant : throw new AutomationException(InvalidPropertyValue, "Invalid property value");
            }

            if (type == typeof(FormColor))
            {
                return FormColor.TryFromOle(Convert.ToInt32(Unwrapped(value), culture), out var color)
                    ? color
                    : throw new AutomationException(InvalidPropertyValue, "Invalid property value");
            }

            return Convert.ChangeType(Unwrapped(value), type, culture);
        }
        catch (OverflowException)
        {
            throw new AutomationException(Overflow, "Overflow");
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException)
        {
            throw new AutomationException(TypeMismatch, "Type mismatch");
        }
    }

    // CS0618: a CurrencyWrapper is how the automation servers' values tell a Currency from a Decimal.
#pragma warning disable CS0618
    private static object? Unwrapped(object? value) => value is CurrencyWrapper currency ? currency.WrappedObject : value;

    private static object CurrencyOf(decimal value) => new CurrencyWrapper(value);
#pragma warning restore CS0618

    // what an event is raised with, as the session's side of it is given it.
    public static object? ToNeutral(FormValue value, Func<ComponentInstance, object> handleOf) => value.Kind switch
    {
        FormValueKind.Empty => null,
        FormValueKind.Null => DBNull.Value,
        FormValueKind.Boolean => value.AsBoolean(),
        FormValueKind.Byte => value.AsByte(),
        FormValueKind.Integer => value.AsInteger(),
        FormValueKind.Long => value.AsLong(),
        FormValueKind.LongLong => value.AsLongLong(),
        FormValueKind.Single => value.AsSingle(),
        FormValueKind.Double => value.AsDouble(),
        FormValueKind.Currency => CurrencyOf(value.AsCurrency()),
        FormValueKind.Decimal => value.AsDecimal(),
        FormValueKind.Date => value.AsDate(),
        FormValueKind.String => value.AsString(),
        FormValueKind.Error => new ErrorWrapper(value.AsError()),
        FormValueKind.Object => value.AsObject() is ComponentInstance component ? handleOf(component) : value.AsObject(),
        _ => null,
    };

    // what a handler left in an argument, as the kind the event raised it with: an argument it cannot hold is left as it was raised.
    public static FormValue FromNeutral(object? value, FormValue raised)
    {
        try
        {
            var invariant = CultureInfo.InvariantCulture;
            // a Boolean is -1 or 0 as a number, as VBA's own coercion makes it.
            var unwrapped = Unwrapped(value) is bool truth && raised.Kind is not FormValueKind.Boolean and not FormValueKind.String ? (truth ? -1 : 0) : Unwrapped(value);
            return raised.Kind switch
            {
                FormValueKind.Boolean => FormValue.FromBoolean(Convert.ToBoolean(unwrapped, invariant)),
                FormValueKind.Byte => FormValue.FromByte(Convert.ToByte(unwrapped, invariant)),
                FormValueKind.Integer => FormValue.FromInteger(Convert.ToInt16(unwrapped, invariant)),
                FormValueKind.Long => FormValue.FromLong(Convert.ToInt32(unwrapped, invariant)),
                FormValueKind.LongLong => FormValue.FromLongLong(Convert.ToInt64(unwrapped, invariant)),
                FormValueKind.Single => FormValue.FromSingle(Convert.ToSingle(unwrapped, invariant)),
                FormValueKind.Double => FormValue.FromDouble(Convert.ToDouble(unwrapped, invariant)),
                FormValueKind.Currency => FormValue.FromCurrency(Convert.ToDecimal(unwrapped, invariant)),
                FormValueKind.Decimal => FormValue.FromDecimal(Convert.ToDecimal(unwrapped, invariant)),
                FormValueKind.Date => FormValue.FromDate(Convert.ToDateTime(unwrapped, invariant)),
                FormValueKind.String => FormValue.FromString(Convert.ToString(unwrapped, invariant) ?? string.Empty),
                _ => raised,
            };
        }
        catch (Exception exception) when (exception is OverflowException or FormatException or InvalidCastException)
        {
            return raised;
        }
    }
}
