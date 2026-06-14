using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using RDCore.SDK.Server;

namespace RDCore.SDK.Client;

/// <summary>
/// A <em>builder</em> that configures LSP handlers for this client app.
/// </summary>
/// <param name="Options">The LSP language client options.</param>
public class RDCoreLanguageClientHandlersConfigurationBuilder(LanguageClientOptions Options) : IRDCoreLSPHandlerConfigurationBuilder
{
    private LanguageClientOptions Options { get; } = Options;

    IRDCoreLSPHandlerConfigurationBuilder IRDCoreLSPHandlerConfigurationBuilder.WithHandler<THandler>()
    {
        Options.WithHandler<THandler>(new() { RequestProcessType = RequestProcessType.Parallel });
        return this;
    }
}
