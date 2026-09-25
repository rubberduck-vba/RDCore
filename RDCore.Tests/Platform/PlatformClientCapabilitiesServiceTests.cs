using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services;

namespace RDCore.Tests.Platform;

/// <summary>
/// The expectations half of the platform handshake: what a connected client asked this server to
/// serve, which is what makes an optional request family negotiable rather than ambient.
/// </summary>
[TestClass]
public sealed class PlatformClientCapabilitiesServiceTests
{
    private static PlatformInitializeParams Handshake(CorePlatformClientCapabilities expected) => new()
    {
        ExpectedComponent = CoreServerComponent.LanguageServer,
        Expected = expected,
    };

    [TestMethod]
    public void BeforeTheHandshake_NothingIsExpected()
    {
        var sut = new PlatformClientCapabilitiesService();

        Assert.IsFalse(sut.IsHandshakeCompleted);
        Assert.IsNull(sut.ExpectedComponent);
        Assert.IsNull(sut.Expected.LanguageServer);
        Assert.IsFalse(sut.Expects(capabilities => capabilities.SessionStatus));
    }

    [TestMethod]
    public void Record_KeepsTheExpectedComponentAndCapabilities()
    {
        var sut = new PlatformClientCapabilitiesService();

        sut.Record(Handshake(new()
        {
            LanguageServer = new LanguageServerCapabilities { SessionStatus = new SessionStatus(true) },
        }));

        Assert.IsTrue(sut.IsHandshakeCompleted);
        Assert.AreEqual(CoreServerComponent.LanguageServer, sut.ExpectedComponent);
        Assert.IsTrue(sut.Expects(capabilities => capabilities.SessionStatus));
    }

    [TestMethod]
    public void Expects_ACapabilitySentAsUnsupported_IsNotExpected()
    {
        var sut = new PlatformClientCapabilitiesService();

        sut.Record(Handshake(new()
        {
            LanguageServer = new LanguageServerCapabilities { SessionStatus = new SessionStatus(false) },
        }));

        Assert.IsTrue(sut.IsHandshakeCompleted);
        Assert.IsFalse(sut.Expects(capabilities => capabilities.SessionStatus));
    }

    [TestMethod]
    public void Expects_AClientThatSentNoLanguageServerGroupAtAll_IsNotExpected()
    {
        var sut = new PlatformClientCapabilitiesService();

        sut.Record(Handshake(new() { Parsing = new ParserCapabilities() }));

        Assert.IsTrue(sut.IsHandshakeCompleted);
        Assert.IsFalse(sut.Expects(capabilities => capabilities.SessionStatus));
    }

    [TestMethod]
    public void Record_ALaterHandshake_ReplacesTheEarlierOne()
    {
        var sut = new PlatformClientCapabilitiesService();

        sut.Record(Handshake(new()
        {
            LanguageServer = new LanguageServerCapabilities { SessionStatus = new SessionStatus(true) },
        }));
        sut.Record(Handshake(new()));

        Assert.IsFalse(sut.Expects(capabilities => capabilities.SessionStatus));
    }
}
