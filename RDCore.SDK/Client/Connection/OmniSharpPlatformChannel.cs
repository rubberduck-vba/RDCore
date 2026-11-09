using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using System.Text.Json;

namespace RDCore.SDK.Client.Connection;

/// <summary>
/// The <see cref="IRDCorePlatformChannel"/> of an OmniSharp language client: what <c>rdc.exe</c> and every <see cref="RDCoreClientApp"/> speak the platform's
/// protocol through, so that they run the same client logic (<see cref="RDCorePlatformClient"/>) as a client that has only <c>RDCore.SDK.Protocol</c>.
/// </summary>
/// <remarks>
/// <para>
/// The JSON is handed to OmniSharp as written by <see cref="RDCorePlatformJson.Options"/>, and handed back as OmniSharp read it: Newtonsoft only carries it, so the
/// wire is the Protocol package's whatever OmniSharp's serializer would have made of the types.
/// </para>
/// <para>
/// 👉 A string that looks like an ISO 8601 date and time (19 to 40 characters, a digit first, a <c>T</c> at the eleventh) is written as it is, but is not always
/// <em>read</em> as it was: OmniSharp parses every incoming message with a reader of its own that takes such a string for a date, whatever its serializer says, and
/// writes it back as the date it read. One with an offset, a <c>Z</c> or a fraction of a second then arrives otherwise (<c>"…12:34:56+02:00"</c> in the offset of the
/// machine, <c>"….000Z"</c> without its fraction); one without, as VBA's <c>Format</c> writes them, arrives as it was. A channel that does not read through OmniSharp
/// has no such change.
/// </para>
/// </remarks>
/// <param name="client">The language client, connected.</param>
/// <param name="logger">Logs each request and how it ended.</param>
/// <param name="peer">What the client is connected to, as the log names it.</param>
/// <param name="lost">
/// Completes when the process the client is connected to exits: a request it has not answered by then fails with a <see cref="ServerProtocolSdkException"/>.
/// <see langword="null"/> for a client whose peer is not a process of its own.
/// </param>
internal sealed class OmniSharpPlatformChannel(ILanguageClient client, ILogger? logger = null, string? peer = null, Task? lost = null) : IRDCorePlatformChannel
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly HashSet<string> _registered = new(StringComparer.Ordinal);

    // LSP 3.17 ErrorCodes: the request was valid and refused; the content it was about changed while it was answered.
    private const int RequestFailedCode = -32803;
    private const int ContentModifiedCode = -32801;

    /// <inheritdoc/>
    public async Task<JsonElement?> SendRequestAsync(string method, JsonElement? parameters, CancellationToken token)
    {
        _logger.LogInformation("→ {method} ({component})", method, peer);
        try
        {
            var answer = parameters is { } json
                ? client.SendRequest(method, ToToken(json)).Returning<JToken?>(token)
                : client.SendRequest(method).Returning<JToken?>(token);
            var result = lost is null ? await answer : await RequestsInFlight.AnsweredAsync(answer, lost, method);
            _logger.LogInformation("← {method} completed", method);
            return ToElement(result);
        }
        catch (Exception exception) when (token.IsCancellationRequested)
        {
            _logger.LogDebug(exception, "← {method} cancelled", method);
            throw;
        }
        catch (OmniSharp.Extensions.JsonRpc.Server.JsonRpcException exception) when (exception.ErrorCode is RequestFailedCode or ContentModifiedCode)
        {
            // answered, not faulted: the server refused the request, or an edit overtook it.
            _logger.LogDebug("← {method} answered {code}: {message}", method, exception.ErrorCode, exception.Message);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "← {method} faulted", method);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task SendNotificationAsync(string method, JsonElement? parameters, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (parameters is { } json)
        {
            client.SendNotification(method, ToToken(json));
        }
        else
        {
            client.SendNotification(method);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public IDisposable OnRequest(string method, Func<JsonElement?, CancellationToken, Task<JsonElement?>> handler)
        => Registered(method, () => client.Register(registry => registry.OnJsonRequest(method, async (JToken parameters, CancellationToken token) =>
        {
            try
            {
                return await handler(ToElement(parameters), token) is { } result ? ToToken(result) : JValue.CreateNull();
            }
            catch (RDCorePlatformInvalidParamsException exception)
            {
                throw new RpcErrorException(RDCorePlatformInvalidParamsException.ErrorCode, null!, exception.Message);
            }
        })));

    /// <inheritdoc/>
    public IDisposable OnNotification(string method, Func<JsonElement?, CancellationToken, Task> handler)
        => Registered(method, () => client.Register(registry => registry.OnJsonNotification(method, (JToken parameters, CancellationToken token) => handler(ToElement(parameters), token))));

    // OmniSharp keeps the first handler of a method and ignores another, and disposing the first leaves the method with none: a second one is refused instead.
    private IDisposable Registered(string method, Func<IDisposable> register)
    {
        lock (_registered)
        {
            if (!_registered.Add(method))
            {
                throw new InvalidOperationException($"'{method}' is already answered on this channel: dispose its registration before registering another.");
            }
        }

        try
        {
            return new Registration(this, method, register());
        }
        catch
        {
            Unregister(method);
            throw;
        }
    }

    private void Unregister(string method)
    {
        lock (_registered)
        {
            _registered.Remove(method);
        }
    }

    private sealed class Registration(OmniSharpPlatformChannel channel, string method, IDisposable registration) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                registration.Dispose();
                channel.Unregister(method);
            }
        }
    }

    private static JToken ToToken(JsonElement json)
    {
        // read as written: a string that looks like a date stays the string it is, on the way out (the way in is the serializer's, see the remarks).
        using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(json.GetRawText())) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
        return JToken.ReadFrom(reader);
    }

    private static JsonElement? ToElement(JToken? token)
        => token is null || token.Type is JTokenType.Null or JTokenType.Undefined
            ? null
            : JsonSerializer.Deserialize<JsonElement>(token.ToString(Newtonsoft.Json.Formatting.None));
}
