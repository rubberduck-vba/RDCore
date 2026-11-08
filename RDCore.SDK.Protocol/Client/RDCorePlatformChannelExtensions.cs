using System.Text.Json;

namespace RDCore.SDK.Client;

/// <summary>
/// Sends and answers typed <c>rdcore/*</c> messages over an <see cref="IRDCorePlatformChannel"/>, written and read with <see cref="RDCorePlatformJson.Options"/>.
/// </summary>
public static class RDCorePlatformChannelExtensions
{
    /// <summary>
    /// Sends a request and reads its result.
    /// </summary>
    /// <typeparam name="TParams">The type of the request's parameters.</typeparam>
    /// <typeparam name="TResult">The type of its result.</typeparam>
    /// <param name="channel">The connection to the server.</param>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The result the server answered.</returns>
    /// <exception cref="JsonException">The server answered <c>null</c>, or a result that is not a <typeparamref name="TResult"/>.</exception>
    public static async Task<TResult> SendRequestAsync<TParams, TResult>(this IRDCorePlatformChannel channel, string method, TParams parameters, CancellationToken token)
    {
        var result = await channel.SendRequestAsync(method, JsonSerializer.SerializeToElement(parameters, RDCorePlatformJson.Options), token).ConfigureAwait(false);
        return Read<TResult>(result, method, "result");
    }

    /// <summary>
    /// Sends a notification.
    /// </summary>
    /// <typeparam name="TParams">The type of the notification's parameters.</typeparam>
    /// <param name="channel">The connection to the server.</param>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="token">Cancels before the notification is sent.</param>
    /// <returns>A task that completes once the notification is sent.</returns>
    public static Task SendNotificationAsync<TParams>(this IRDCorePlatformChannel channel, string method, TParams parameters, CancellationToken token)
        => channel.SendNotificationAsync(method, JsonSerializer.SerializeToElement(parameters, RDCorePlatformJson.Options), token);

    /// <summary>
    /// Answers the requests of a method the server sends the client.
    /// </summary>
    /// <typeparam name="TParams">The type of the request's parameters.</typeparam>
    /// <typeparam name="TResult">The type of its result.</typeparam>
    /// <param name="channel">The connection to the server.</param>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="handler">Answers a request.</param>
    /// <returns>The registration; disposing it stops answering the method.</returns>
    /// <remarks>
    /// A request sent with no <c>params</c> is read as <c>{}</c>: the parameters with every member at its default. A request whose <c>params</c> are not a
    /// <typeparamref name="TParams"/> is not given to the handler: it fails with <see cref="RDCorePlatformInvalidParamsException"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The method is already answered on the channel.</exception>
    public static IDisposable OnRequest<TParams, TResult>(this IRDCorePlatformChannel channel, string method, Func<TParams, CancellationToken, Task<TResult>> handler)
        => channel.OnRequest(method, async (parameters, token) =>
        {
            var result = await handler(ReadParameters<TParams>(parameters, method), token).ConfigureAwait(false);
            return JsonSerializer.SerializeToElement(result, RDCorePlatformJson.Options);
        });

    /// <summary>
    /// Handles the notifications of a method the server sends the client.
    /// </summary>
    /// <typeparam name="TParams">The type of the notification's parameters.</typeparam>
    /// <param name="channel">The connection to the server.</param>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="handler">Handles a notification.</param>
    /// <returns>The registration; disposing it stops handling the method.</returns>
    /// <remarks>
    /// A notification sent with no <c>params</c> is read as <c>{}</c>; one whose <c>params</c> are not a <typeparamref name="TParams"/> fails with
    /// <see cref="RDCorePlatformInvalidParamsException"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The method is already handled on the channel.</exception>
    public static IDisposable OnNotification<TParams>(this IRDCorePlatformChannel channel, string method, Func<TParams, CancellationToken, Task> handler)
        => channel.OnNotification(method, (parameters, token) => handler(ReadParameters<TParams>(parameters, method), token));

    private static TParams ReadParameters<TParams>(JsonElement? parameters, string method)
    {
        try
        {
            return parameters is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) }
                ? Read<TParams>(parameters, method, "params")
                : JsonSerializer.Deserialize<TParams>("{}", RDCorePlatformJson.Options)!;
        }
        catch (JsonException exception)
        {
            throw new RDCorePlatformInvalidParamsException(method, exception);
        }
    }

    private static T Read<T>(JsonElement? json, string method, string what)
        => json is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } value
            ? value.Deserialize<T>(RDCorePlatformJson.Options) ?? throw new JsonException($"The {what} of {method} is null.")
            : throw new JsonException($"The {what} of {method} is null.");
}
