using System.Text.Json;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// <see cref="System.Text.Json"/> (de)serialization for rich platform payloads that ride a JSON-RPC
/// method as an opaque string.
/// </summary>
/// <remarks>
/// The JSON-RPC transport (OmniSharp) serializes with <c>Newtonsoft.Json</c>, which does not
/// understand the model's <c>[JsonPolymorphic]</c>/<c>[JsonDerivedType]</c> types (the AST) or its
/// custom converters (<c>VBTypedValueJsonConverter</c>). Payloads that use those are serialized here
/// with <see cref="System.Text.Json"/> and carried inside a <see cref="PlatformJsonEnvelope"/> whose
/// only wire field is a string the transport passes through untouched.
/// </remarks>
public static class PlatformJson
{
    private static readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Serializes <paramref name="payload"/> to a <see cref="System.Text.Json"/> string.
    /// </summary>
    public static string Serialize<T>(T payload) => JsonSerializer.Serialize(payload, _options);

    /// <summary>
    /// Deserializes a <see cref="System.Text.Json"/> string produced by <see cref="Serialize{T}"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The string deserialized to <c>null</c>.</exception>
    public static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, _options)
            ?? throw new InvalidOperationException($"The payload deserialized to a null {typeof(T).Name}.");
}

/// <summary>
/// A JSON-RPC request or response body whose real payload is a <see cref="System.Text.Json"/> string
/// (see <see cref="PlatformJson"/>). Flat by design so the transport's own serializer round-trips it.
/// </summary>
public record class PlatformJsonEnvelope
{
    /// <summary>
    /// The <see cref="System.Text.Json"/> representation of the payload.
    /// </summary>
    public string Json { get; init; } = string.Empty;

    /// <summary>
    /// Wraps <paramref name="payload"/> in an envelope.
    /// </summary>
    public static PlatformJsonEnvelope Of<T>(T payload) => new() { Json = PlatformJson.Serialize(payload) };

    /// <summary>
    /// Deserializes the wrapped payload as <typeparamref name="T"/>.
    /// </summary>
    public T Unwrap<T>() => PlatformJson.Deserialize<T>(Json);
}
