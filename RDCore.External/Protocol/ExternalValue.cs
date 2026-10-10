using RDCore.External.Automation;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RDCore.External.Protocol;

/// <summary>
/// What a value that crosses to or from the external host is.
/// </summary>
/// <remarks>
/// The neutral values of <see cref="IAutomationServer"/>, one kind each: a server tells apart what the language tells apart - a <c>Currency</c> from a <c>Decimal</c>, an
/// <c>Error</c> from a <c>Long</c> - and so does the wire.
/// </remarks>
public enum ExternalValueKind
{
    /// <summary>An <c>Empty</c>: <see langword="null"/>.</summary>
    Empty,

    /// <summary>A <c>Null</c>: <see cref="DBNull"/>.</summary>
    Null,

    /// <summary>An argument that was left out: <see cref="Missing"/>.</summary>
    Missing,

    /// <summary>An object reference to no object: an empty <see cref="DispatchWrapper"/>.</summary>
    Nothing,

    /// <summary>A <see cref="bool"/>.</summary>
    Boolean,

    /// <summary>A <see cref="byte"/>.</summary>
    Byte,

    /// <summary>A <see cref="sbyte"/>, which a server may return and the language has no type of.</summary>
    SByte,

    /// <summary>A <see cref="short"/>.</summary>
    Integer,

    /// <summary>A <see cref="ushort"/>, which a server may return and the language has no type of.</summary>
    UInt16,

    /// <summary>An <see cref="int"/>.</summary>
    Long,

    /// <summary>A <see cref="uint"/>, which a server may return and the language has no type of.</summary>
    UInt32,

    /// <summary>A <see cref="long"/>.</summary>
    LongLong,

    /// <summary>A <see cref="ulong"/>, which a server may return and the language has no type of.</summary>
    UInt64,

    /// <summary>A <see cref="float"/>.</summary>
    Single,

    /// <summary>A <see cref="double"/>.</summary>
    Double,

    /// <summary>A <c>Currency</c>: a <see cref="CurrencyWrapper"/>.</summary>
    Currency,

    /// <summary>A <see cref="decimal"/>.</summary>
    Decimal,

    /// <summary>A <see cref="DateTime"/>.</summary>
    Date,

    /// <summary>A <see cref="string"/>.</summary>
    String,

    /// <summary>An <c>Error</c>: an <see cref="ErrorWrapper"/>.</summary>
    Error,

    /// <summary>An object the external host holds, by its handle.</summary>
    Object,

    /// <summary>An array, with its bounds and its elements.</summary>
    Array,

    /// <summary>The element type of an array of anything: an array of <see cref="object"/>.</summary>
    Variant,
}

/// <summary>
/// A value that crosses to or from the external host.
/// </summary>
public sealed record class ExternalValue
{
    /// <summary>
    /// What the value is.
    /// </summary>
    public ExternalValueKind Kind { get; init; }

    /// <summary>
    /// A scalar, written in the invariant culture in a form that reads back as the same value; <see langword="null"/> for any other kind.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// The handle of an <see cref="ExternalValueKind.Object"/>.
    /// </summary>
    public long Handle { get; init; }

    /// <summary>
    /// What the elements of an <see cref="ExternalValueKind.Array"/> are declared to be.
    /// </summary>
    public ExternalValueKind ElementKind { get; init; }

    /// <summary>
    /// The lower bound of each dimension of an <see cref="ExternalValueKind.Array"/>.
    /// </summary>
    public int[]? LowerBounds { get; init; }

    /// <summary>
    /// The length of each dimension of an <see cref="ExternalValueKind.Array"/>.
    /// </summary>
    public int[]? Lengths { get; init; }

    /// <summary>
    /// The elements of an <see cref="ExternalValueKind.Array"/>, the first subscript varying fastest: the order a <c>SAFEARRAY</c> is laid out in.
    /// </summary>
    public ExternalValue[]? Elements { get; init; }
}

/// <summary>
/// Writes the neutral values of <see cref="IAutomationServer"/> to the wire and reads them back.
/// </summary>
/// <remarks>
/// A value reads back as the value it was written as, of the same type: the two ends of the wire speak the same neutral values, and only an object is not what it was -
/// it is the handle of the object, which only the end that holds the object can turn back into it.
/// </remarks>
public static class ExternalValues
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// Writes a value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="handleOf">The handle of an object, which is how an object crosses.</param>
    public static ExternalValue ToWire(object? value, Func<object, long> handleOf) => value switch
    {
        null => new() { Kind = ExternalValueKind.Empty },
        DBNull => new() { Kind = ExternalValueKind.Null },
        Missing => new() { Kind = ExternalValueKind.Missing },
        DispatchWrapper { WrappedObject: null } => new() { Kind = ExternalValueKind.Nothing },
        DispatchWrapper wrapper => new() { Kind = ExternalValueKind.Object, Handle = handleOf(wrapper.WrappedObject) },
        bool boolean => Scalar(ExternalValueKind.Boolean, boolean ? bool.TrueString : bool.FalseString),
        byte @byte => Scalar(ExternalValueKind.Byte, @byte.ToString(Invariant)),
        sbyte @sbyte => Scalar(ExternalValueKind.SByte, @sbyte.ToString(Invariant)),
        short integer => Scalar(ExternalValueKind.Integer, integer.ToString(Invariant)),
        ushort @ushort => Scalar(ExternalValueKind.UInt16, @ushort.ToString(Invariant)),
        int @long => Scalar(ExternalValueKind.Long, @long.ToString(Invariant)),
        uint @uint => Scalar(ExternalValueKind.UInt32, @uint.ToString(Invariant)),
        long longLong => Scalar(ExternalValueKind.LongLong, longLong.ToString(Invariant)),
        ulong @ulong => Scalar(ExternalValueKind.UInt64, @ulong.ToString(Invariant)),
        float single => Scalar(ExternalValueKind.Single, single.ToString("R", Invariant)),
        double @double => Scalar(ExternalValueKind.Double, @double.ToString("R", Invariant)),
        decimal @decimal => Scalar(ExternalValueKind.Decimal, @decimal.ToString(Invariant)),
        CurrencyWrapper currency => Scalar(ExternalValueKind.Currency, ((decimal)currency.WrappedObject).ToString(Invariant)),
        // a date is the number of days it is from the epoch of OLE Automation, which is all a VT_DATE ever is.
        DateTime date => Scalar(ExternalValueKind.Date, date.ToOADate().ToString("R", Invariant)),
        string text => Scalar(ExternalValueKind.String, text),
        ErrorWrapper error => Scalar(ExternalValueKind.Error, error.ErrorCode.ToString(Invariant)),
        Array array => ToWire(array, handleOf),
        _ => new() { Kind = ExternalValueKind.Object, Handle = handleOf(value) },
    };

    private static ExternalValue Scalar(ExternalValueKind kind, string text) => new() { Kind = kind, Text = text };

    private static ExternalValue ToWire(Array array, Func<object, long> handleOf)
    {
        var lowerBounds = Enumerable.Range(0, array.Rank).Select(array.GetLowerBound).ToArray();
        var lengths = Enumerable.Range(0, array.Rank).Select(array.GetLength).ToArray();
        return new()
        {
            Kind = ExternalValueKind.Array,
            ElementKind = KindOf(array.GetType().GetElementType()!),
            LowerBounds = lowerBounds,
            Lengths = lengths,
            Elements = [.. Subscripts(lowerBounds, lengths).Select(subscripts => ToWire(array.GetValue(subscripts), handleOf))],
        };
    }

    /// <summary>
    /// Reads a value back.
    /// </summary>
    /// <param name="value">The value, as it crossed.</param>
    /// <param name="objectOf">The object a handle is the handle of.</param>
    public static object? FromWire(ExternalValue value, Func<long, object> objectOf) => value.Kind switch
    {
        ExternalValueKind.Empty => null,
        ExternalValueKind.Null => DBNull.Value,
        ExternalValueKind.Missing => Missing.Value,
        ExternalValueKind.Nothing => new DispatchWrapper(null),
        ExternalValueKind.Boolean => bool.Parse(value.Text!),
        ExternalValueKind.Byte => byte.Parse(value.Text!, Invariant),
        ExternalValueKind.SByte => sbyte.Parse(value.Text!, Invariant),
        ExternalValueKind.Integer => short.Parse(value.Text!, Invariant),
        ExternalValueKind.UInt16 => ushort.Parse(value.Text!, Invariant),
        ExternalValueKind.Long => int.Parse(value.Text!, Invariant),
        ExternalValueKind.UInt32 => uint.Parse(value.Text!, Invariant),
        ExternalValueKind.LongLong => long.Parse(value.Text!, Invariant),
        ExternalValueKind.UInt64 => ulong.Parse(value.Text!, Invariant),
        ExternalValueKind.Single => float.Parse(value.Text!, Invariant),
        ExternalValueKind.Double => double.Parse(value.Text!, Invariant),
        ExternalValueKind.Decimal => decimal.Parse(value.Text!, Invariant),
        ExternalValueKind.Currency => new CurrencyWrapper(decimal.Parse(value.Text!, Invariant)),
        ExternalValueKind.Date => DateTime.FromOADate(double.Parse(value.Text!, Invariant)),
        ExternalValueKind.String => value.Text ?? string.Empty,
        ExternalValueKind.Error => new ErrorWrapper(int.Parse(value.Text!, Invariant)),
        ExternalValueKind.Object => objectOf(value.Handle),
        ExternalValueKind.Array => ArrayFromWire(value, objectOf),
        _ => throw new AutomationException(TypeMismatch, string.Format(CultureInfo.CurrentCulture, ExternalMessages.ValueCannotCross, value.Kind)),
    };

    private const int TypeMismatch = unchecked((int)0x80020005);

    private static Array ArrayFromWire(ExternalValue value, Func<long, object> objectOf)
    {
        var lowerBounds = value.LowerBounds ?? [];
        var lengths = value.Lengths ?? [];
        var result = System.Array.CreateInstance(TypeOf(value.ElementKind), lengths, lowerBounds);
        var elements = value.Elements ?? [];

        var index = 0;
        foreach (var subscripts in Subscripts(lowerBounds, lengths))
        {
            result.SetValue(FromWire(elements[index++], objectOf), subscripts);
        }

        return result;
    }

    // the element types the neutral values have arrays of; an array of anything else crosses as an array of anything.
    private static ExternalValueKind KindOf(Type elementType) => Type.GetTypeCode(elementType) switch
    {
        TypeCode.Boolean => ExternalValueKind.Boolean,
        TypeCode.Byte => ExternalValueKind.Byte,
        TypeCode.SByte => ExternalValueKind.SByte,
        TypeCode.Int16 => ExternalValueKind.Integer,
        TypeCode.UInt16 => ExternalValueKind.UInt16,
        TypeCode.Int32 => ExternalValueKind.Long,
        TypeCode.UInt32 => ExternalValueKind.UInt32,
        TypeCode.Int64 => ExternalValueKind.LongLong,
        TypeCode.UInt64 => ExternalValueKind.UInt64,
        TypeCode.Single => ExternalValueKind.Single,
        TypeCode.Double => ExternalValueKind.Double,
        TypeCode.Decimal => ExternalValueKind.Decimal,
        TypeCode.DateTime => ExternalValueKind.Date,
        TypeCode.String => ExternalValueKind.String,
        _ => ExternalValueKind.Variant,
    };

    private static Type TypeOf(ExternalValueKind elementKind) => elementKind switch
    {
        ExternalValueKind.Boolean => typeof(bool),
        ExternalValueKind.Byte => typeof(byte),
        ExternalValueKind.SByte => typeof(sbyte),
        ExternalValueKind.Integer => typeof(short),
        ExternalValueKind.UInt16 => typeof(ushort),
        ExternalValueKind.Long => typeof(int),
        ExternalValueKind.UInt32 => typeof(uint),
        ExternalValueKind.LongLong => typeof(long),
        ExternalValueKind.UInt64 => typeof(ulong),
        ExternalValueKind.Single => typeof(float),
        ExternalValueKind.Double => typeof(double),
        ExternalValueKind.Decimal => typeof(decimal),
        ExternalValueKind.Date => typeof(DateTime),
        ExternalValueKind.String => typeof(string),
        _ => typeof(object),
    };

    // the subscripts of every element of an array, the first varying fastest.
    private static IEnumerable<int[]> Subscripts(int[] lowerBounds, int[] lengths)
    {
        if (lengths.Length == 0 || lengths.Any(length => length == 0))
        {
            yield break;
        }

        var current = (int[])lowerBounds.Clone();
        while (true)
        {
            yield return [.. current];

            var dimension = 0;
            while (dimension < current.Length && ++current[dimension] >= lowerBounds[dimension] + lengths[dimension])
            {
                current[dimension] = lowerBounds[dimension];
                dimension++;
            }

            if (dimension == current.Length)
            {
                yield break;
            }
        }
    }
}
