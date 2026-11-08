using System.Text.Json;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Extensibility.Diagnostics;

// A code travels as it is written ("VBC09300"), which is what a reader of a payload expects to see, and what the code type parses.
internal sealed class RDCoreDiagnosticCodeJsonConverter : JsonConverter<RDCoreDiagnosticCode>
{
    public override RDCoreDiagnosticCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => RDCoreDiagnosticCode.TryParse(reader.GetString(), out var code)
            ? code
            : throw new JsonException($"'{reader.GetString()}' is not a diagnostic code: three letters A to Z, and five digits.");

    public override void Write(Utf8JsonWriter writer, RDCoreDiagnosticCode value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
