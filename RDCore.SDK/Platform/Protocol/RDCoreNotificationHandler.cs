using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.SDK.ConsoleIO;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// The base class for a platform protocol JSON-RPC notification.
/// </summary>
/// <typeparam name="TNotification">A serializable type representing the notification parameters.</typeparam>
/// <remarks>
/// A <c>[Method]</c> attribute must be specified on the implementing class to map the handler to a specific platform protocol notification.
/// </remarks>
public abstract class RDCoreNotificationHandler<TNotification> : IJsonRpcHandler, IJsonRpcNotificationHandler<TNotification>
    where TNotification : IRequest
{
    public async Task<Unit> Handle(TNotification request, CancellationToken cancellationToken)
    {
        await HandleNotificationAsync(request, cancellationToken);
        return Unit.Value;
    }

    protected abstract Task HandleNotificationAsync(TNotification request, CancellationToken token);
}

/// <summary>
/// The base class for a platform protocol JSON-RPC request.
/// </summary>
/// <typeparam name="TNotification">A serializable type representing the request parameters.</typeparam>
/// <remarks>
/// A <c>[Method]</c> attribute must be specified on the implementing class to map the handler to a specific platform protocol request.
/// </remarks>
public abstract class RDCoreRequestHandler<TRequest, TResponse> : IJsonRpcHandler, IJsonRpcRequestHandler<TRequest, TResponse>
    where TRequest : IRequest, IRequest<TResponse>
{
    // whatever HandleAsync throws must reach the client as a JSON-RPC error carrying no build-machine
    // path — OmniSharp's invoker would otherwise drop the raw exception.ToString() into the response.
    public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await HandleAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // cancellation is control flow, not a fault
            throw;
        }
        catch (Exception exception) when (exception is RequestException or RpcErrorException)
        {
            // already a protocol error, with its own code and a vetted message
            throw;
        }
        catch (Exception exception)
        {
            throw new RpcErrorException(
                InternalErrorCode, error: null!, SourcePathAnonymizer.Scrub(exception.ToString()));
        }
    }

    // JSON-RPC 2.0 "Internal error"
    private const int InternalErrorCode = -32603;

    protected abstract Task<TResponse> HandleAsync(TRequest request, CancellationToken token);
}
