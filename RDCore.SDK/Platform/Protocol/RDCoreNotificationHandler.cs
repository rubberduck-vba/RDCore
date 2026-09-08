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
    /// <summary>
    /// Invokes <see cref="HandleAsync"/> and guarantees that whatever it throws reaches the client as
    /// a well-formed JSON-RPC error whose message carries no build-machine source path. OmniSharp's
    /// request invoker would otherwise put an unexpected exception's <see cref="Exception.ToString"/>
    /// — a PDB build's absolute paths and all — straight into the <c>-32603</c> response.
    /// </summary>
    public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await HandleAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // cancellation is a normal control-flow signal, not a fault to sanitize.
            throw;
        }
        catch (Exception exception) when (exception is RequestException or RpcErrorException)
        {
            // already a protocol error: it carries its own code and a message the thrower vetted.
            throw;
        }
        catch (Exception exception)
        {
            throw new RpcErrorException(
                InternalErrorCode, error: null!, SourcePathAnonymizer.Scrub(exception.ToString()));
        }
    }

    // JSON-RPC 2.0 "Internal error".
    private const int InternalErrorCode = -32603;

    protected abstract Task<TResponse> HandleAsync(TRequest request, CancellationToken token);
}
