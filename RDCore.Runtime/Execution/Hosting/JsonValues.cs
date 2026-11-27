using RDCore.Runtime.Execution.External.Scripting;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDCore.Runtime.Execution.Hosting;

// the JSON value of a value of the language, and the value of the language a JSON value is, for a parameter of a declared type: what a program invoked by
// name is given as its arguments, and gives back as its result.
//
//   Scripting.Dictionary   an object, its keys Strings (any other key is 13); a JSON object is a new Dictionary that compares its keys binary.
//   array                  any array, a multi-dimensional one as nested arrays, the first dimension outermost, its bounds dropped; a JSON array is a
//                          Variant() from 0, and a nested one a Variant() in it; for an array of another element type, 13.
//   Date                   ISO 8601 text to the second, with no offset (2026-11-26T14:30:00); any other text, one with an offset or a zone
//                          included, is 13. In a Variant, such text stays a String.
//   Currency, Decimal      a number, every digit of it.
//   Empty, Null, Nothing   null; null is Null in a Variant, Nothing in an object, 94 in any other type.
//   Collection             an array of its members; any other object is 13.
//   a number in a Variant  what a literal of the same digits is (MS-VBAL §3.3.2): Integer, then Long, then Double past Long's magnitude; with a fraction or
//                          an exponent, Double.
//
// Into any other type, a JSON value is let-coerced from that value of the language, as an assignment is (MS-VBAL §5.5.1.2), with the errors it raises:
// 13 for text that is not a number, 6 past the type's range. A value that has no conversion at all, either way, is 13.
internal sealed class JsonValues(IRuntimeSession session, IRuntimeSymbolResolver resolver)
{
    private const string Moment = "yyyy'-'MM'-'dd'T'HH':'mm':'ss";

    // what a conversion refuses, which unwinds a value nested in another to the error the conversion is.
    private sealed class Refused(VBRuntimeErrorId error, string verbose) : Exception(verbose)
    {
        public VBRuntimeErrorId Error { get; } = error;
    }

    // the JSON value of value; the error it is instead, when it has none.
    public VBRuntimeErrorInfo? ToJson(VBTypedValue value, out JsonNode? json)
    {
        json = null;
        try
        {
            json = Out(value, []);
            return null;
        }
        catch (Refused refused)
        {
            return VBRuntimeErrorInfo.For(refused.Error, default, refused.Message);
        }
    }

    // the value of the declared type that json is; the error it is instead, when it is none.
    public VBRuntimeErrorInfo? FromJson(JsonNode? json, VBType declared, out VBTypedValue? value)
    {
        value = null;
        try
        {
            value = In(json, declared);
            return null;
        }
        catch (Refused refused)
        {
            return VBRuntimeErrorInfo.For(refused.Error, default, refused.Message);
        }
    }

    // the objects on the way to the value written: one the way reaches again holds itself, and has no JSON value.
    private JsonNode? Out(VBTypedValue value, HashSet<VBRuntimeObjectId> way) => value switch
    {
        VBVariantValue variant => Out(variant.TypedValue, way),
        VBEmptyValue or VBNullValue => null,
        VBBooleanValue boolean => JsonValue.Create((bool)boolean.Value),
        VBByteValue @byte => JsonValue.Create(@byte.Value),
        VBIntegerValue integer => JsonValue.Create(integer.Value),
        VBLongValue @long => JsonValue.Create(@long.Value),
        VBLongLongValue longLong => JsonValue.Create(longLong.Value),
        VBSingleValue single => float.IsFinite(single.Value) ? JsonValue.Create(single.Value) : throw NoNumber(),
        VBDoubleValue @double => double.IsFinite(@double.Value) ? JsonValue.Create(@double.Value) : throw NoNumber(),
        VBCurrencyValue currency => JsonValue.Create(currency.Value.Value),
        VBDecimalValue @decimal => JsonValue.Create(@decimal.Value),
        VBDateValue date => ValueConversions.TryMomentOf(date, out var moment)
            ? JsonValue.Create(moment.ToString(Moment, CultureInfo.InvariantCulture))
            : throw Mismatch("A Date that is not a day from January 1, 100 to December 31, 9999 has no JSON value."),
        VBStringValue text => JsonValue.Create(text.Value ?? string.Empty),
        VBObjectValue reference => reference.IsNothing() ? null : OutObject(reference.Value, way),
        VBArrayValue array => OutArray(array, 0, new int[array.Rank], way),
        _ => throw Mismatch($"A value of type '{value.TypeInfo.Name}' has no JSON value."),
    };

    private static Refused NoNumber() => Mismatch("A number that is not finite has no JSON value.");

    // the elements of one dimension at the subscripts before it, each an array of the next dimension's.
    private JsonArray OutArray(VBArrayValue array, int dimension, int[] subscripts, HashSet<VBRuntimeObjectId> way)
    {
        var result = new JsonArray();
        if (!array.IsInitialized)
        {
            return result;
        }

        var (lower, upper) = array.Dimensions[dimension];
        for (var subscript = lower; subscript <= upper; subscript++)
        {
            subscripts[dimension] = subscript;
            result.Add(dimension < array.Rank - 1
                ? OutArray(array, dimension + 1, subscripts, way)
                : Out(array[subscripts] ?? throw Mismatch("An element of an array cannot be read."), way));
        }

        return result;
    }

    private JsonNode OutObject(VBRuntimeObjectId identity, HashSet<VBRuntimeObjectId> way)
    {
        if (!way.Add(identity))
        {
            throw Mismatch("An object that holds itself has no JSON value.");
        }

        try
        {
            if (session.ExternalObjects.TryGet(identity, out var owner, out var handle)
                && owner is ScriptingCallProvider && handle is ScriptingDictionary dictionary)
            {
                return OutDictionary(dictionary, way);
            }

            if (session.Symbols.TryGetInstance(identity, out var instance) && StdCollection.IsCollection(instance.ClassModule))
            {
                var members = new JsonArray();
                foreach (var member in SessionLibraryObjects.Of(session).Collections.ValuesOf(instance))
                {
                    members.Add(Out(member, way));
                }

                return members;
            }

            throw Mismatch("An object that is neither a Dictionary nor a Collection has no JSON value.");
        }
        finally
        {
            way.Remove(identity);
        }
    }

    // a JSON object's names are Strings, which no other key can become without merging keys a Dictionary keeps apart (1 and "1").
    private JsonObject OutDictionary(ScriptingDictionary dictionary, HashSet<VBRuntimeObjectId> way)
    {
        var result = new JsonObject();
        foreach (var key in dictionary.KeyList())
        {
            if (Unwrapped(key) is not VBStringValue name)
            {
                throw Mismatch($"A Dictionary key of type '{Unwrapped(key).TypeInfo.Name}' is not a JSON name.");
            }

            dictionary.Item(key, out var item);
            result[name.Value ?? string.Empty] = Out(item, way);
        }

        return result;
    }

    private VBTypedValue In(JsonNode? json, VBType declared) => declared switch
    {
        VBVariantType => new VBVariantValue(Natural(json)),
        VBObjectType or VBClassType => json switch
        {
            null => VBObjectValue.Nothing,
            JsonObject record when declared is VBObjectType || ScriptingCallProvider.IsDictionaryClass(((VBClassType)declared).Symbol) => Dictionary(record),
            _ => throw Mismatch($"A JSON {KindOf(json)} is not a '{declared.Name}'."),
        },
        _ when json is null => throw new Refused(VBRuntimeErrorId.InvalidUseOfNull, $"A JSON null is not a '{declared.Name}'."),
        VBArrayType { ItemType: VBVariantType } when json is JsonArray list => Array(list),
        VBNumericType number when json is JsonValue raw && raw.GetValueKind() == JsonValueKind.Number => Coerced(ValueConversions.ToNumeric(
            declared is IFixedPointNumericType ? Exact(raw) : Number(raw), number), declared),
        VBDateType when json is JsonValue raw && raw.GetValueKind() == JsonValueKind.String => Date(raw.GetValue<string>()),
        VBNumericType or VBBooleanType or VBStringType or VBDateType when json is JsonValue => Coerced(declared switch
        {
            VBNumericType number => ValueConversions.ToNumeric(Natural(json), number),
            VBBooleanType => ValueConversions.ToBoolean(Natural(json)),
            VBDateType => ValueConversions.ToDate(Natural(json)),
            _ => ValueConversions.ToText(Natural(json)),
        }, declared),
        _ => throw Mismatch($"A JSON {KindOf(json)} is not a '{declared.Name}'."),
    };

    // the value a JSON value is, as a Variant holds it.
    private VBTypedValue Natural(JsonNode? json) => json switch
    {
        null => VBNullValue.Null,
        JsonObject record => Dictionary(record),
        JsonArray list => Array(list),
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.True => new VBBooleanValue(true),
            JsonValueKind.False => new VBBooleanValue(false),
            JsonValueKind.Number => Number(value),
            _ => new VBStringValue(value.GetValue<string>()),
        },
        _ => throw Mismatch($"A JSON {KindOf(json)} has no value in the language."),
    };

    // MS-VBAL §3.3.2: an integer literal is an Integer, a Long past Integer's magnitude, and a Double past Long's; the sign is not part of the literal, so
    // -32768 is a Long, as x = -32768 makes it. A fraction or an exponent makes a Double.
    private static VBTypedValue Number(JsonValue value)
    {
        var text = value.ToJsonString();
        if (text.AsSpan().IndexOfAny('.', 'e', 'E') < 0
            && long.TryParse(text.AsSpan().TrimStart('-'), NumberStyles.None, CultureInfo.InvariantCulture, out var magnitude))
        {
            var sign = text.StartsWith('-') ? -1 : 1;
            if (magnitude <= short.MaxValue)
            {
                return new VBIntegerValue((short)(sign * magnitude));
            }

            if (magnitude <= int.MaxValue)
            {
                return new VBLongValue((int)(sign * magnitude));
            }
        }

        var real = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return double.IsFinite(real) ? new VBDoubleValue(real) : throw Mismatch($"The JSON number {text} has no Double.");
    }

    // a number for a Currency or a Decimal, every digit of it: through a Double, digits past its fifteenth would be lost.
    private static VBTypedValue Exact(JsonValue value)
    {
        var text = value.ToJsonString();
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact)
            ? new VBDecimalValue(exact)
            : throw new Refused(VBRuntimeErrorId.Overflow, $"The JSON number {text} has no Decimal.");
    }

    // ISO 8601 with no offset: a Date has no zone (MS-VBAL §2.1), and reading an offset would depend on the zone the program runs in.
    private static VBDateValue Date(string text)
        => DateTime.TryParseExact(text, Moment, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)
            ? new VBDateValue(moment.ToOADate())
            : throw Mismatch($"The text '{text}' is not ISO 8601 date and time with no offset.");

    private static VBTypedValue Coerced(ValueConversionResult conversion, VBType declared)
        => conversion.IsSuccess
            ? conversion.Value!
            : throw new Refused(conversion.Error ?? VBRuntimeErrorId.TypeMismatch, $"The JSON value is not a '{declared.Name}'.");

    private VBObjectValue Dictionary(JsonObject record)
    {
        var (made, dictionary) = ScriptingCallProvider.NewDictionary(session, resolver);
        foreach (var (name, item) in record)
        {
            dictionary.Add(new VBStringValue(name), Natural(item));
        }

        return made;
    }

    private VBResizableArrayValue Array(JsonArray list)
    {
        var array = new VBResizableArrayValue([(0, list.Count - 1)], VBVariantType.TypeInfo);
        for (var index = 0; index < list.Count; index++)
        {
            array.TrySetElement(ElementCells.OfVariant(new VBVariantValue(Natural(list[index]))), index);
        }

        return array;
    }

    private static string KindOf(JsonNode? json) => json switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        _ => json.GetValueKind() switch
        {
            JsonValueKind.Number => "number",
            JsonValueKind.String => "string",
            _ => "boolean",
        },
    };

    private static VBTypedValue Unwrapped(VBTypedValue value)
    {
        while (value is VBVariantValue { TypedValue: { } held })
        {
            value = held;
        }

        return value;
    }

    private static Refused Mismatch(string verbose) => new(VBRuntimeErrorId.TypeMismatch, verbose);
}
