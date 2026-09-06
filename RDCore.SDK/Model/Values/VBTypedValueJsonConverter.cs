using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Model.Values;

/// <summary>
/// Serializes a <see cref="VBTypedValue"/> as <c>{ "type": &lt;name&gt;, "value": &lt;scalar&gt; }</c>.
/// </summary>
/// <remarks>
/// 👉 The only <see cref="VBTypedValue"/> that crosses a process boundary is the one an AST resolves
/// for a <em>literal</em> — always a compile-time scalar or a sentinel. Reference-valued kinds
/// (objects, arrays, UDTs, Variants) carry identity / storage semantics, never appear in an AST, and
/// are rejected here.
/// </remarks>
public sealed class VBTypedValueJsonConverter : JsonConverter<VBTypedValue>
{
    private const string TypeProperty = "type";
    private const string ValueProperty = "value";

    public override VBTypedValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var typeName = root.TryGetProperty(TypeProperty, out var typeElement)
            ? typeElement.GetString()
            : throw new JsonException($"A serialized {nameof(VBTypedValue)} requires a '{TypeProperty}' property.");

        var hasValue = root.TryGetProperty(ValueProperty, out var value);

        return typeName switch
        {
            VBTypeNames.VBBoolean => new VBBooleanValue(value.GetBoolean()),
            VBTypeNames.VBByte => new VBByteValue(value.GetByte()),
            VBTypeNames.VBInteger => new VBIntegerValue(value.GetInt16()),
            VBTypeNames.VBLong => new VBLongValue(value.GetInt32()),
            VBTypeNames.VBLongLong => new VBLongLongValue(value.GetInt64()),
            VBTypeNames.VBSingle => new VBSingleValue(value.GetSingle()),
            VBTypeNames.VBDouble => new VBDoubleValue(value.GetDouble()),
            VBTypeNames.VBCurrency => new VBCurrencyValue(value.GetDecimal()),
            VBTypeNames.VBDecimal => new VBDecimalValue(value.GetDecimal()),
            VBTypeNames.VBDate => new VBDateValue(value.GetDouble()),
            VBTypeNames.VBString => new VBStringValue(hasValue && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty),
            VBTypeNames.VBError => new VBErrorValue(hasValue ? value.GetInt32() : 0),
            VBTypeNames.VBNull => VBNullValue.Null,
            VBTypeNames.VBEmpty => VBEmptyValue.Empty,
            VBTypeNames.VBMissing => VBMissingValue.Missing,
            VBTypeNames.VBUnknown => VBUnknownValue.DefaultValue,
            VBTypeNames.VBVoid => VBVoidValue.Void,
            VBTypeNames.VBObject => VBObjectValue.Nothing,
            _ => throw new JsonException($"'{typeName}' is not a serializable literal value type."),
        };
    }

    public override void Write(Utf8JsonWriter writer, VBTypedValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(TypeProperty, value.TypeInfo.Name);

        switch (value)
        {
            case VBBooleanValue v: writer.WriteBoolean(ValueProperty, (bool)v.Value); break;
            case VBByteValue v: writer.WriteNumber(ValueProperty, v.Value); break;
            case VBIntegerValue v: writer.WriteNumber(ValueProperty, v.Value); break;
            case VBLongValue v: writer.WriteNumber(ValueProperty, v.Value); break;
            case VBLongLongValue v: writer.WriteNumber(ValueProperty, v.Value); break;
            case VBSingleValue v: writer.WriteNumber(ValueProperty, v.Value); break;
            case VBDoubleValue v: writer.WriteNumber(ValueProperty, v.Value); break;
            case VBCurrencyValue v: writer.WriteNumber(ValueProperty, v.Value.Value); break;
            case VBDecimalValue v: writer.WriteNumber(ValueProperty, v.Value); break;
            case VBDateValue v: writer.WriteNumber(ValueProperty, v.SerialValue); break;
            case VBStringValue v: writer.WriteString(ValueProperty, v.Value); break;
            case VBErrorValue v: writer.WriteNumber(ValueProperty, v.Value); break;
            case VBNullValue or VBEmptyValue or VBMissingValue or VBUnknownValue or VBVoidValue or VBNothingValue:
                break; // sentinel — the type name is the whole payload
            default:
                throw new JsonException($"'{value.GetType().Name}' is not a serializable literal value (only scalars and sentinels appear in an AST).");
        }

        writer.WriteEndObject();
    }
}
