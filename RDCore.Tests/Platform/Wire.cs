using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using RDCore.SDK.Platform.Components;

namespace RDCore.Tests.Platform;

/// <summary>
/// The JSON-RPC hop a request makes between two processes of the platform, without the processes.
/// </summary>
internal static class Wire
{
    /// <summary>
    /// What the other end reads: <paramref name="payload"/> as the transport's own (<c>Newtonsoft.Json</c>) serializer writes it and reads it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A payload the transport cannot write is never sent and never answered, and the platform waits for the answer: a harness that hands the handler its
    /// request as an object never finds that out.
    /// </para>
    /// <para>
    /// A <see cref="Uri"/> is written as the string it was made of, and read back as relative or absolute: one made of a path (<c>/tmp/ws</c>) is read
    /// back relative on Linux. The platform's own are <c>file:</c> URIs (<see cref="FileUri"/>), which are absolute on any platform.
    /// </para>
    /// <para>
    /// The symbols of a module that are not read back say which end was overwritten (<see cref="FailureOf"/>).
    /// </para>
    /// </remarks>
    public static T RoundTrip<T>(T payload) where T : notnull
    {
        var written = LspSerializer.Instance.SerializeObject(payload);
        try
        {
            return LspSerializer.Instance.DeserializeObject<T>(written);
        }
        catch (Newtonsoft.Json.JsonException failure) when (payload is DefineSymbolsParams symbols)
        {
            throw new InvalidOperationException(FailureOf(symbols, written), failure);
        }
    }

    /// <summary>
    /// What a round trip of the symbols of a module that was not read back says of itself. The <c>SymbolsJson</c> written on the wire is parsed again, from an
    /// array of its own: when it parses, it was written whole, and the buffer the reader rented from the shared pool was overwritten while it read it; when it
    /// does not, the writer's buffer was, and the message holds what was written beside a new serialization of the same symbols. Bytes of something else in
    /// what was written say what overwrote them.
    /// </summary>
    /// <param name="symbols">The request whose round trip failed.</param>
    /// <param name="written">The request as the transport wrote it.</param>
    internal static string FailureOf(DefineSymbolsParams symbols, string written)
    {
        string? wire;
        try
        {
            wire = Newtonsoft.Json.Linq.JObject.Parse(written).GetValue(nameof(DefineSymbolsParams.SymbolsJson), StringComparison.OrdinalIgnoreCase) is { } token
                ? (string?)token
                : null;
        }
        catch (Newtonsoft.Json.JsonException exception)
        {
            return $"The symbols of module {symbols.ModuleName} were not read back, and the request the transport wrote is not JSON ({exception.Message}): {written}";
        }

        if (wire is null)
        {
            return $"The symbols of module {symbols.ModuleName} were not read back, and the request the transport wrote has no {nameof(DefineSymbolsParams.SymbolsJson)}: {written}";
        }

        string again;
        try
        {
            again = symbols.SymbolsJson;
        }
        catch (Exception exception)
        {
            again = $"(not serialized again: {exception.Message})";
        }

        if (ParseFailureOf(wire) is not { } failure)
        {
            return $"The symbols of module {symbols.ModuleName} were not read back, but the {wire.Length} characters written of them are JSON"
                + $"{(string.Equals(wire, again, StringComparison.Ordinal) ? ", as a new serialization writes them" : $", and not what a new serialization writes ({again.Length} characters)")}:"
                + " the reader's buffer, rented from the shared pool, was overwritten while it read them.";
        }

        return $"The symbols of module {symbols.ModuleName} were not read back: the {wire.Length} characters written of them are not JSON ({failure}), so the"
            + $" writer's buffer, rented from the shared pool, was overwritten while it wrote them.{Environment.NewLine}Written: {wire}{Environment.NewLine}"
            + $"Written again: {again}";
    }

    // the reason json is not JSON, read from an array of its own and not one of the shared pool; null when it is.
    private static string? ParseFailureOf(string json)
    {
        var reader = new System.Text.Json.Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
        try
        {
            while (reader.Read())
            {
            }

            return null;
        }
        catch (System.Text.Json.JsonException exception)
        {
            return exception.Message;
        }
    }

    /// <summary>
    /// The <c>file:</c> URI of a path, written as one: what a client sends the platform as its workspace root.
    /// </summary>
    public static Uri FileUri(string path) => new(new Uri(path).AbsoluteUri);
}
