using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Globalization;

namespace RDCore.Runtime.Execution.Debugging;

/// <summary>
/// A value, as a debugger shows it: what it says, what type it is, and the parts it has.
/// </summary>
/// <param name="Display">
/// The value as the debugger writes it: a string between quotes, a date between <c>#</c>, <c>True</c>, <c>Nothing</c>. Empty for a value that is its parts.
/// </param>
/// <param name="Type">The type: <c>Long</c>, <c>Variant/String</c>, <c>Long(1 To 3)</c>.</param>
/// <param name="Parts">The parts of an array or a user-defined type, or <see langword="null"/> for a value that has none.</param>
public sealed record class DebugValueView(string Display, string Type, IReadOnlyList<DebugPart>? Parts);

/// <summary>
/// A part of a value: an element of an array, a field of a user-defined type.
/// </summary>
/// <param name="Name">The name of the field, or the subscripts of the element between parentheses.</param>
/// <param name="Value">The part's value.</param>
public sealed record class DebugPart(string Name, VBTypedValue Value);

/// <summary>
/// Describes a value for a debugger.
/// </summary>
/// <remarks>
/// This is not <c>Print</c>: a string is quoted because the person looking at a variable wants to see that it is one, and a number is not padded. It is also total - a value
/// it does not know how to write is written as its type, and a value that cannot be read at all is written as what it is - since the variables of a program that is
/// stopped half way include some that nothing has put a value in yet.
/// </remarks>
public static class DebugValueFormatter
{
    /// <summary>The most parts of one value that are listed; an array of a million elements is not a thing to put on a screen.</summary>
    public const int MaxParts = 200;

    /// <summary>
    /// Describes a value.
    /// </summary>
    /// <param name="value">The value.</param>
    public static DebugValueView Describe(VBTypedValue value)
    {
        try
        {
            return Of(value, wrappedIn: null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidCastException or KeyNotFoundException or NullReferenceException or ArgumentException)
        {
            // a variable nothing has allocated yet is not one the debugger can read; it is not an error of the program.
            return new DebugValueView("<unavailable>", value.TypeInfo.Name, null);
        }
    }

    private static DebugValueView Of(VBTypedValue value, string? wrappedIn)
    {
        string Type(string name) => wrappedIn is null ? name : $"{wrappedIn}/{name}";

        switch (value)
        {
            case VBVariantValue variant:
                return Of(variant.TypedValue, wrappedIn: "Variant");

            case VBEmptyValue:
                return new DebugValueView("Empty", Type("Empty"), null);

            case VBNullValue:
                return new DebugValueView("Null", Type("Null"), null);

            case VBNothingValue:
                return new DebugValueView("Nothing", Type("Object"), null);

            case VBMissingValue:
                return new DebugValueView("<missing>", Type("Missing"), null);

            case VBErrorValue error:
                return new DebugValueView($"Error {error.Value}", Type("Error"), null);

            case VBBooleanValue boolean:
                return new DebugValueView(boolean.Value.StoredValue != 0 ? "True" : "False", Type("Boolean"), null);

            case VBStringValue text:
                return new DebugValueView($"\"{text.Value.Replace("\"", "\"\"")}\"", Type(text.TypeInfo.Name), null);

            case VBDateValue date:
                return new DebugValueView(Date(date.Value), Type("Date"), null);

            case VBObjectValue obj:
                return new DebugValueView(obj.IsNothing() ? "Nothing" : "<Object>", Type("Object"), null);

            case VBArrayValue array:
                return Array(array, Type);

            case VBUserDefinedTypeValue record:
                return new DebugValueView(
                    string.Empty,
                    Type(record.TypeInfo.Name),
                    [.. record.Fields.Select((field, index) => new DebugPart(field.Name, record.FieldAt(index)!)).Where(part => part.Value is not null)]);

            default:
                return new DebugValueView(Scalar(value), Type(value.TypeInfo.Name), null);
        }
    }

    private static DebugValueView Array(VBArrayValue array, Func<string, string> type)
    {
        var element = array.ItemType.Name;
        if (!array.IsInitialized)
        {
            return new DebugValueView("<not dimensioned>", type($"{element}()"), null);
        }

        var bounds = string.Join(", ", array.Dimensions.Select(dimension => $"{dimension.LowerBound} To {dimension.UpperBound}"));
        var parts = new List<DebugPart>();
        foreach (var subscripts in Subscripts(array))
        {
            if (parts.Count >= MaxParts || array[subscripts] is not { } part)
            {
                break;
            }

            parts.Add(new DebugPart($"({string.Join(", ", subscripts)})", part));
        }

        return new DebugValueView(string.Empty, type($"{element}({bounds})"), parts);
    }

    // every subscript of the array, the last dimension varying fastest: the order its elements are listed in.
    private static IEnumerable<int[]> Subscripts(VBArrayValue array)
    {
        var current = array.Dimensions.Select(dimension => dimension.LowerBound).ToArray();
        while (true)
        {
            yield return [.. current];

            var dimension = current.Length - 1;
            while (dimension >= 0)
            {
                if (++current[dimension] <= array.Dimensions[dimension].UpperBound)
                {
                    break;
                }

                current[dimension] = array.Dimensions[dimension].LowerBound;
                dimension--;
            }

            if (dimension < 0)
            {
                yield break;
            }
        }
    }

    // #1/2/2000# and #1/2/2000 1:30:00 PM#: a date as the VBA editor shows a date literal, which is the invariant culture's.
    private static string Date(DateTime date)
        => date.TimeOfDay == TimeSpan.Zero
            ? $"#{date.ToString("M/d/yyyy", CultureInfo.InvariantCulture)}#"
            : $"#{date.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture)}#";

    private static string Scalar(VBTypedValue value)
        => Convert.ToString(value.RuntimeValue.BoxedValue, CultureInfo.InvariantCulture) ?? string.Empty;
}
