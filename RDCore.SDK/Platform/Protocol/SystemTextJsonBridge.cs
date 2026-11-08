using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Writes and reads a message of <c>RDCore.SDK.Protocol</c> through <c>System.Text.Json</c> and <see cref="RDCorePlatformJson.Options"/>, where OmniSharp's
/// Newtonsoft serializer would get it wrong.
/// </summary>
/// <remarks>
/// <para>
/// OmniSharp writes the JSON-RPC wire with Newtonsoft, which knows nothing of the <c>System.Text.Json</c> attributes the Protocol types carry
/// (a <c>JsonStringEnumConverter</c>) nor of <see cref="System.Text.Json.JsonElement"/>, which it would write as its own properties. A type that has either is
/// given to this converter, by a Newtonsoft <c>[JsonConverter]</c> on its SDK request or result wrapper: the wrapper then reaches the wire exactly as a
/// client that has only the Protocol package writes it.
/// </para>
/// <para>
/// The converter is named on the type, not added to a serializer's converters, so that it holds on every connection the type travels, whatever serializer
/// that connection was built with: OmniSharp's <c>LspSerializer</c> replaces its settings while a connection initializes.
/// </para>
/// </remarks>
/// <typeparam name="T">The wrapper type.</typeparam>
public sealed class SystemTextJsonBridge<T> : JsonConverter
{
    /// <inheritdoc/>
    public override bool CanConvert(Type objectType) => typeof(T).IsAssignableFrom(objectType);

    /// <inheritdoc/>
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteRawValue(System.Text.Json.JsonSerializer.Serialize(value, value.GetType(), RDCorePlatformJson.Options));
    }

    /// <inheritdoc/>
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var token = JToken.ReadFrom(reader);
        return token.Type == JTokenType.Null
            ? null
            : System.Text.Json.JsonSerializer.Deserialize(token.ToString(Newtonsoft.Json.Formatting.None), objectType, RDCorePlatformJson.Options);
    }
}
