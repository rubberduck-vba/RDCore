using RDCore.Runtime.Execution.External.Scripting;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.LanguageCore.StdLib.Abstract;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDCore.Runtime.Execution.Hosting.Json;

// the JSON value of a value of the language, and the value of the language a JSON value is, for a parameter of a declared type: what a program invoked by
// name is given as its arguments, and gives back as its result.
//
//   Scripting.Dictionary   an object, its keys Strings (any other key is 13); a JSON object is a new Dictionary that compares its keys binary.
//   array                  any array, a multi-dimensional one as nested arrays, the first dimension outermost, its bounds dropped; a JSON array is a
//                          Variant() from 0, and a nested one a Variant() in it. For a dynamic array of another element type, a JSON array is an
//                          array of exactly that type from 0, each element converted to it. A fixed-size array, which is not a let target, and an
//                          array of a UDT are 13.
//   Date                   ISO 8601 text to the second, with no offset (2026-11-26T14:30:00); any other text, one with an offset or a zone
//                          included, is 13. In a Variant, such text stays a String.
//   Currency, Decimal      a number, every digit of it.
//   Empty, Null, Nothing   null; null is Null in a Variant, Nothing in an object, 13 in an array or a UDT, 94 in any other type (MS-VBAL
//                          §5.5.1.2.10; a fixed-size array is not a let target, for null as for any value).
//   Collection             an array of its members; a JSON array is a New Collection of its elements, each what a Variant holds of it.
//   an instance            of a class of the workspace: an object of its public members, read as CallByName reads them (JsonValues.Instances.cs); a
//                          JSON object is a New object of the class, given each key's value as CallByName gives a member one.
//                          Any other object is 13.
//   a number in a Variant  what a literal of the same digits is (MS-VBAL §3.3.2): Integer, then Long, then Double past Long's magnitude; with a fraction or
//                          an exponent, Double.
//
// Into any other type, a JSON value is let-coerced from that value of the language, as an assignment is (MS-VBAL §5.5.1.2), with the errors it raises:
// 13 for text that is not a number, 6 past the type's range. A value that has no conversion at all, either way, is 13.
internal sealed partial class JsonValues(IRuntimeSession session, IRuntimeSymbolResolver resolver)
{
    private const string Moment = "yyyy'-'MM'-'dd'T'HH':'mm':'ss";

    // what a conversion refuses, which unwinds a value nested in another to the error the conversion is.
    private sealed class Refused(VBRuntimeErrorId error, string verbose) : Exception(verbose)
    {
        public VBRuntimeErrorId Error { get; } = error;
    }

    // the JSON value of value; the error it is instead, when it has none, or the error a member it reads raised, as the member raised it.
    public IVBRaisableError? ToJson(VBTypedValue value, out JsonNode? json)
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
        catch (Raised raised)
        {
            return raised.Error;
        }
    }

    // the value of the declared type that json is; the error it is instead, when it is none, or the error a member it gives a value to raised, as the
    // member raised it.
    public IVBRaisableError? FromJson(JsonNode? json, VBType declared, out VBTypedValue? value)
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
        catch (Raised raised)
        {
            return raised.Error;
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
            : throw Mismatch(Exceptions.JsonValues_DateOutOfRange_Verbose),
        VBStringValue text => JsonValue.Create(text.Value ?? string.Empty),
        VBObjectValue reference => reference.IsNothing() ? null : OutObject(reference, way),
        VBArrayValue array => OutArray(array, 0, new int[array.Rank], way),
        _ => throw Mismatch(Verbose(Exceptions.JsonValues_TypeHasNoJson_Verbose, value.TypeInfo.Name)),
    };

    private static Refused NoNumber() => Mismatch(Exceptions.JsonValues_NotFinite_Verbose);

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
                : Out(array[subscripts] ?? throw Mismatch(Exceptions.JsonValues_ElementUnreadable_Verbose), way));
        }

        return result;
    }

    private JsonNode OutObject(VBObjectValue reference, HashSet<VBRuntimeObjectId> way)
    {
        var identity = reference.Value;
        if (!way.Add(identity))
        {
            throw Mismatch(Exceptions.JsonValues_HoldsItself_Verbose);
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

            if (instance is not null && IsWorkspaceClass(instance.ClassModule))
            {
                return OutInstance(reference, instance.ClassModule, way);
            }

            throw Mismatch(Exceptions.JsonValues_ObjectHasNoJson_Verbose);
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
                throw Mismatch(Verbose(Exceptions.JsonValues_KeyNotAName_Verbose, Unwrapped(key).TypeInfo.Name));
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
            JsonObject record when declared is VBClassType { Symbol: var classModule } && IsWorkspaceClass(classModule) => Instance(record, classModule),
            JsonArray list when declared is VBClassType { Symbol: var classModule } && StdCollection.IsCollection(classModule) => Collection(list, classModule),
            _ => throw Mismatch(Verbose(Exceptions.JsonValues_KindNotType_Verbose, KindOf(json), declared.Name)),
        },
        // MS-VBAL §5.2.3.4: a member of an enumeration is a Long.
        VBEnumType => In(json, VBLongType.TypeInfo),
        // MS-VBAL §5.5.1.2.10: Null let to a resizable array or a UDT is 13, the last arm's error; to any other type, 94. A fixed-size array,
        // which is not a let target, is 13 too.
        _ when json is null && declared is not (VBArrayType or VBUserDefinedType)
            => throw new Refused(VBRuntimeErrorId.InvalidUseOfNull, Verbose(Exceptions.JsonValues_NullNotType_Verbose, declared.Name)),
        // a fixed-size array is not a let target: a JSON array for one is 13, the last arm's error.
        VBResizableArrayType { ItemType: VBVariantType } when json is JsonArray list => Array(list),
        VBResizableArrayType { ItemType: not VBUserDefinedType and var item } when json is JsonArray list => Array(list, item),
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
        _ => throw Mismatch(Verbose(Exceptions.JsonValues_KindNotType_Verbose, KindOf(json), declared.Name)),
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
        _ => throw Mismatch(Verbose(Exceptions.JsonValues_KindHasNoValue_Verbose, KindOf(json))),
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
        return double.IsFinite(real) ? new VBDoubleValue(real) : throw Mismatch(Verbose(Exceptions.JsonValues_NoDouble_Verbose, text));
    }

    // a number for a Currency or a Decimal, every digit of it: through a Double, digits past its fifteenth would be lost.
    private static VBTypedValue Exact(JsonValue value)
    {
        var text = value.ToJsonString();
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact)
            ? new VBDecimalValue(exact)
            : throw new Refused(VBRuntimeErrorId.Overflow, Verbose(Exceptions.JsonValues_NoDecimal_Verbose, text));
    }

    // ISO 8601 with no offset: a Date has no zone (MS-VBAL §2.1), and reading an offset would depend on the zone the program runs in.
    private static VBDateValue Date(string text)
        => DateTime.TryParseExact(text, Moment, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)
            ? new VBDateValue(moment.ToOADate())
            : throw Mismatch(Verbose(Exceptions.JsonValues_NotIsoDate_Verbose, text));

    private static VBTypedValue Coerced(ValueConversionResult conversion, VBType declared)
        => conversion.IsSuccess
            ? conversion.Value!
            : throw new Refused(conversion.Error ?? VBRuntimeErrorId.TypeMismatch, Verbose(Exceptions.JsonValues_ValueNotType_Verbose, declared.Name));

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

        return Bound(array);
    }

    // an array made here, bound to its runtime value as a variable's array is: what an array parameter or a Property Let is given is that value.
    private static VBResizableArrayValue Bound(VBResizableArrayValue array)
        => (VBResizableArrayValue)array.WithRuntimeValue(SymbolAddressTable.BoxedValue(array));

    // an array of exactly the declared element type, in the cells a declared array of it has: a Property Let of an array takes only a dynamic array of its
    // own element type, and a Variant() given to it is 13 (observed in Excel). An element that does not convert is the array's error.
    private VBResizableArrayValue Array(JsonArray list, VBType itemType)
    {
        (int, int)[] dimensions = [(0, list.Count - 1)];
        var cells = ElementCells.Of(itemType);
        var array = itemType is VBByteType
            ? new VBResizableByteArrayValue(dimensions, cells)
            : new VBResizableArrayValue(dimensions, itemType, elementCell: cells);
        for (var index = 0; index < list.Count; index++)
        {
            ElementCells.Store(array, cells, In(list[index], itemType).RuntimeValue, isKnown: true, index);
        }

        return Bound(array);
    }

    // a New Collection, each element added to it as Add adds an item: called by name, what a Variant holds of the element (an object a Dictionary).
    private VBObjectValue Collection(JsonArray list, VBClassModuleSymbol classModule)
    {
        var created = RuntimeExpressionEvaluator.CreateObject(session, classModule);
        if (!created.IsSuccess || created.Result is not VBObjectValue collection)
        {
            throw Unwound(created.ErrorInfo, Verbose(Exceptions.JsonValues_InitializeEnded_Verbose, classModule.Name));
        }

        var interaction = new StdInteraction(session);
        foreach (var item in list)
        {
            var added = interaction.CallByName(collection, new VBStringValue("Add"), VBCallType.VBMethod, new VBVariantValue(Natural(item)));
            if (!added.IsSuccess)
            {
                throw Unwound(added.ErrorInfo, Verbose(Exceptions.JsonValues_InitializeEnded_Verbose, classModule.Name));
            }
        }

        return collection;
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

    private static string Verbose(string format, params object[] arguments) => string.Format(CultureInfo.CurrentCulture, format, arguments);
}
