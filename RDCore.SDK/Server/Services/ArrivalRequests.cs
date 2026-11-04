using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.JsonRpc.Server.Messages;

namespace RDCore.SDK.Server.Services;

/// <summary>
/// Handles the requests marked <see cref="HandledOnArrivalAttribute"/> as they arrive, beside the requests OmniSharp is handling in its order.
/// </summary>
/// <remarks>
/// <para>
/// OmniSharp hands every request it receives to a scheduler that handles it once those before it are done, and builds that scheduler itself: a server cannot
/// give it another. What it receives is first read by the server's <see cref="IReceiver"/> (<see cref="RDCoreLifecycleReceiver"/>), and a request the receiver
/// keeps is never scheduled: it is handled here, through the same handlers, and answered as OmniSharp answers a request.
/// </para>
/// <para>
/// A <c>$/cancelRequest</c> does not reach a request handled here: OmniSharp knows of the requests it schedules. A request is marked because it is answered at once.
/// </para>
/// </remarks>
internal sealed class ArrivalRequests(IServiceProvider services, ILogger<ArrivalRequests> logger)
{
    private readonly Lazy<IRequestRouter<IHandlerDescriptor?>> _router = new(services.GetRequiredService<IRequestRouter<IHandlerDescriptor?>>);
    private readonly Lazy<IOutputHandler> _output = new(services.GetRequiredService<IOutputHandler>);

    /// <summary>
    /// Handles <paramref name="request"/> if it is marked, and answers it once its handler is done.
    /// </summary>
    /// <param name="request">A request the server received.</param>
    /// <returns>Whether the request is marked, and so is handled here rather than scheduled.</returns>
    public bool TryHandle(Request request)
    {
        var descriptor = _router.Value.GetDescriptors(request);        if (descriptor.Default?.Params?.IsDefined(typeof(HandledOnArrivalAttribute), inherit: true) is not true)
        {
            return false;
        }

        _ = Task.Run(() => AnswerAsync(descriptor, request));
        return true;
    }

    // answered as OmniSharp answers a request it schedules.
    private async Task AnswerAsync(IRequestDescriptor<IHandlerDescriptor?> descriptor, Request request)
    {
        ErrorResponse response;
        try
        {
            response = await _router.Value.RouteRequest(descriptor, request, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            response = new RequestCancelled(request.Id, request.Method);
        }
        catch (RpcErrorException exception)
        {
            response = new RpcError(request.Id, request.Method, new ErrorMessage(exception.Code, exception.Message, exception.Error));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "❌ The request {Method} {RequestId} failed.", request.Method, request.Id);
            response = new InternalError(request.Id, request.Method, ServerErrorAnswers.OneLineOf(exception));
        }

        _output.Value.Send(response.Value);
    }
}
