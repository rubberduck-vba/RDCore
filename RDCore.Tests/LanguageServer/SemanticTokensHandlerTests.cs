using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.SemanticTokens;

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
        var options = OptionsOf(new SemanticTokensHandler(Substitute.For<ISemanticTokensService>()));

        Assert.IsTrue(options.Range is null || options.Range is { IsBool: true, Bool: false }, "a range request is not served");
        Assert.IsTrue(options.Full is { IsValue: true } or { IsBool: true, Bool: true }, "the whole document is served");
    }
}
