using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.LanguageServer.Server;

namespace RDCore.SDK.Server;

/// <summary>
/// What an RDCore server answers a request whose handler fails unexpectedly.
/// </summary>
public static class ServerErrorAnswers
{
    /// <summary>
    /// Has the server answer a request whose handler throws with <c>InternalError</c> (-32603) and the exception's message on one line, and write the whole
    /// exception - its stack and its inner exceptions - to <paramref name="logger"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By default, OmniSharp 0.19.9 answers <c>Internal Error - </c> and the exception's whole <c>ToString()</c>: some forty lines, with the stack and the
    /// paths of the source files the platform was built from, which a client shows its user as it is.
    /// </para>
    /// <para>
    /// A refusal a handler foresees is an error of its own (<c>RpcErrorException</c>, such as <c>RequestFailed</c>, -32803), and is answered as the handler
    /// throws it; so is a request that is cancelled.
    /// </para>
    /// </remarks>
    /// <param name="options">The server's options.</param>
    /// <param name="logger">Where the whole exception is written: the server's own log, not its client.</param>
    /// <returns>The same options.</returns>
    public static LanguageServerOptions WithOneLineErrors(this LanguageServerOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        options.WithServices(services => services
            .AddSingleton(new OneLineErrorLog(logger))
            .AddTransient(typeof(IPipelineBehavior<,>), typeof(OneLineErrorBehavior<,>)));
        return options;
    }

    /// <summary>
    /// The one line an exception is answered with: the first line of its message, or its type when it has none.
    /// </summary>
    public static string OneLineOf(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = exception.Message.Split('\n', 2)[0].Trim();
        return message.Length > 0 ? message : exception.GetType().Name;
    }
}

// the log the whole exception is written to, which the server's handlers are built with.
internal sealed record class OneLineErrorLog(ILogger Logger);

// around every request's handler: an exception the handler did not foresee is answered on one line, and written whole to the server's log.
internal sealed class OneLineErrorBehavior<TRequest, TResponse>(OneLineErrorLog log) : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<TResponse> next)
    {
        try
        {
            return await next();
        }
        // an error of the handler's own (RpcErrorException, which is no JsonRpcException) and a cancelled request are answered as they are.
        catch (Exception exception) when (exception is not (RpcErrorException or JsonRpcException or OperationCanceledException))
        {
            log.Logger.LogError(exception, "❌ The request {request} failed.", typeof(TRequest).Name);
            throw new RpcErrorException(ErrorCodes.InternalError, null!, ServerErrorAnswers.OneLineOf(exception));
        }
    }
}
