using NSubstitute;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.SemanticTokens;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Services;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What the server says it serves of <strong>LSP 3.17</strong> §Semantic Tokens.
/// </summary>
[TestClass]
public sealed class SemanticTokensHandlerTests
{
    private static SemanticTokensRegistrationOptions OptionsOf(SemanticTokensHandler handler)
        => ((IRegistration<SemanticTokensRegistrationOptions, SemanticTokensCapability>)handler)
            .GetRegistrationOptions(new SemanticTokensCapability(), new ClientCapabilities());

    // OmniSharp's builder encoded the first token of a range relative to the range, where LSP has every first token relative to the start of the document, so
    // a client read it as being on the document's first line (HexIDE's findings desk #25): the whole document is cheap to give, and is what is served.
    [TestMethod]
    public void Ranges_AreNotServed_AndTheWholeDocumentIs()
    {
        var options = OptionsOf(new SemanticTokensHandler(Substitute.For<ISemanticTokensService>(), new IncomingDocumentChanges()));

        Assert.IsTrue(options.Range is null || options.Range is { IsBool: true, Bool: false }, "a range request is not served");
        Assert.IsTrue(options.Full is { IsValue: true } or { IsBool: true, Bool: true }, "the whole document is served");
    }

    private static SessionSemanticTokensHandler FragmentHandler(ISemanticTokensService service, bool advertised)
    {
        var capabilities = Substitute.For<IPlatformClientCapabilitiesService>();
        capabilities.Expects(Arg.Any<Func<LanguageServerCapabilities, CorePlatformClientCapability>>()).Returns(advertised);
        return new SessionSemanticTokensHandler(service, capabilities);
    }

    [TestMethod]
    public async Task TheTokensOfFragments_AreEncodedAsTheTokensOfADocument_RelativeToTheTokenBefore()
    {
        var service = Substitute.For<ISemanticTokensService>();
        service.GetFragmentsAsync("Program", "Dim x", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<IReadOnlyList<SemanticToken>>>(
            [
                [new SemanticToken(0, 0, 1, 2, 0), new SemanticToken(0, 4, 3, 5, 1), new SemanticToken(1, 2, 1, 0, 0)],
                [],
            ]));

        var result = await FragmentHandler(service, advertised: true)
            .Handle(new SessionSemanticTokensParams { ModuleName = "Program", Source = "Dim x", Fragments = ["X > 1", string.Empty] }, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { 0, 0, 1, 2, 0, 0, 4, 3, 5, 1, 1, 2, 1, 0, 0 }, result.Fragments[0].Data.ToArray(), "five integers a token, as textDocument/semanticTokens/full");
        Assert.IsEmpty(result.Fragments[1].Data, "a fragment with no token has an empty answer");
    }

    [TestMethod]
    public async Task TheTokensOfFragments_AreRefused_FromAClientThatDidNotAdvertiseTheCapability()
    {
        var service = Substitute.For<ISemanticTokensService>();

        var refusal = await Assert.ThrowsExactlyAsync<RpcErrorException>(() =>
            FragmentHandler(service, advertised: false).Handle(new SessionSemanticTokensParams { Fragments = ["X"] }, CancellationToken.None));

        StringAssert.Contains(refusal.Message, nameof(SessionSemanticTokens));
        await service.DidNotReceiveWithAnyArgs().GetFragmentsAsync(default!, default!, default!, default);
    }
}
