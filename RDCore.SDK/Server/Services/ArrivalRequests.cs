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
/// Whether a request is marked is a fact of its method, and its parameters are read only when it is: the receiver reads what arrives outside the
/// <c>try</c> in which OmniSharp answers a request whose parameters can't be read, and such a request is left to OmniSharp, which answers it so.
/// </para>
/// <para>
/// The marked requests are handled one after the other, in the order they arrived in, and each is begun as it is read - before the message after it is: a
/// pause sent before a run is not a pause of that run. A <c>$/cancelRequest</c> does not reach a request handled here: OmniSharp knows of the requests it
/// schedules.
/// </para>
/// </remarks>
internal sealed class ArrivalRequests(IServiceProvider services, ILogger<ArrivalRequests> logger)
{
    private readonly Lazy<IRequestRouter<IHandlerDescriptor?>> _router = new(services.GetRequiredService<IRequestRouter<IHandlerDescriptor?>>);
    private readonly Lazy<IHandlersManager> _handlers = new(services.GetRequiredService<IHandlersManager>);
    private readonly Lazy<IOutputHandler> _output = new(services.GetRequiredService<IOutputHandler>);
    private readonly object _gate = new();
    private Task _last = Task.CompletedTask;

    /// <summary>
    /// Handles <paramref name="request"/> if it is marked, and answers it once its handler is done.
    /// </summary>
    /// <param name="request">A request the server received.</param>
    /// <returns>Whether the request is marked, and so is handled here rather than scheduled.</returns>
    public bool TryHandle(Request request)
    {
        if (!IsMarked(request.Method))
        {
            return false;
        }

        IRequestDescriptor<IHandlerDescriptor?> descriptor;
        try
        {
            descriptor = _router.Value.GetDescriptors(request);
        }
        catch (Exception exception)
        {
            logger.LogWarning("The parameters of {Method} {RequestId} can't be read ({Reason}); the request is left to be answered so.", request.Method, request.Id, exception.Message);
            return false;
        }

        lock (_gate)
        {
            _last = AnswerAfterAsync(_last, descriptor, request);
        }

        return true;
    }

    // a delegating handler's parameters are the request it delegates, which is the one that is marked.
    private bool IsMarked(string method)
        => _handlers.Value.Descriptors.FirstOrDefault(handler => handler.Method == method) is { Params: { } parameters } handler
            && (handler.IsDelegatingHandler ? parameters.GetGenericArguments().FirstOrDefault() ?? parameters : parameters)
                .IsDefined(typeof(HandledOnArrivalAttribute), inherit: true);

    private async Task AnswerAfterAsync(Task previous, IRequestDescriptor<IHandlerDescriptor?> descriptor, Request request)
    {
        await previous;
        await AnswerAsync(descriptor, request);
    }

    // answered as OmniSharp answers a request it schedules; never faults, so that the requests after it are handled.
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

        try
        {
            _output.Value.Send(response.Value);
        }
        catch (Exception exception)
        {
            logger.LogWarning("The answer to {Method} {RequestId} was not sent ({Reason}).", request.Method, request.Id, exception.Message);
        }
    }
}
