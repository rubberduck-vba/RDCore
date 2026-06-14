using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Server;

namespace RDCore.SDK.Server;

/// <summary>
/// A <em>builder</em> that configures LSP handlers for this server app.
/// </summary>
public interface IRDCoreLSPHandlerConfigurationBuilder
{
    /// <summary>
    /// Configures a <strong>LSP 3.17</strong> (OmniSharp) <em>handler</em> for dependency injection in a <c>RDCore.SDK</c> application.
    /// </summary>
    /// <typeparam name="THandler">The specific concrete implementation type of <em>OmniSharp</em> LSP handler class to register.</typeparam>
    IRDCoreLSPHandlerConfigurationBuilder WithHandler<THandler>() where THandler : class, IJsonRpcHandler;

}


/// <summary>
/// A <em>builder</em> that configures LSP handlers for this server app.
/// </summary>
/// <param name="Options">The LSP language server options.</param>
public class RDCoreLanguageServerHandlersConfigurationBuilder(LanguageServerOptions Options) : IRDCoreLSPHandlerConfigurationBuilder
{
    private LanguageServerOptions Options { get; } = Options;

    /// <summary>
    /// Configures a <strong>LSP 3.17</strong> (OmniSharp) <em>handler</em> for dependency injection in a <c>RDCore.SDK</c> application.
    /// </summary>
    /// <typeparam name="THandler">The specific concrete implementation type of <em>OmniSharp</em> LSP handler class to register.</typeparam>
    IRDCoreLSPHandlerConfigurationBuilder IRDCoreLSPHandlerConfigurationBuilder.WithHandler<THandler>()
    {
        Options.WithHandler<THandler>(new() { RequestProcessType = RequestProcessType.Parallel });
        return this;
    }
}
