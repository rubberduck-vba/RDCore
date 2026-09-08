using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Server.Logging;

namespace RDCore.Tests.Server;

/// <summary>
/// The scrubbing protocol logger stands in for OmniSharp's <c>AddLanguageProtocolLogging()</c>: it
/// forwards the same record text to the client, but a build-machine source path in a logged
/// exception cannot ride <c>window/logMessage</c> off the machine.
/// </summary>
[TestClass]
public sealed class ScrubbingLanguageServerLoggerTests
{
    [TestMethod]
    // OmniSharp's request invoker logs an unhandled handler fault with LogCritical(exception, …); its
    // protocol logger appends exception.ToString() verbatim. Every forwarding mode must strip the path.
    [DataRow(SourcePathScrubMode.RepoRelative, "RDCore.Parsing/ModuleParser.cs:line 51")]
    [DataRow(SourcePathScrubMode.FileName, "ModuleParser.cs:line 51")]
    [DataRow(SourcePathScrubMode.Redacted, "<source>:line 51")]
    public void ComposedMessage_IsScrubbedPerMode(SourcePathScrubMode mode, string expectedFragment)
    {
        var fault = new InvalidOperationException(
            "boom\r\n" +
            "   at RDCore.Parsing.ModuleParser.Parse() in C:\\Users\\somebody\\src\\RDCore\\RDCore.Parsing\\ModuleParser.cs:line 51");

        var composed = ScrubbingLanguageServerLogger.ComposeMessage(
            "RDCore.Parsing.ModuleParser", state: "parse failed", exception: fault, formatter: (state, _) => state);
        var scrubbed = SourcePathAnonymizer.Scrub(composed, mode);

        StringAssert.Contains(scrubbed, expectedFragment);
        Assert.IsFalse(scrubbed.Contains("C:\\"), "the drive-letter prefix must not reach the client");
        Assert.IsFalse(scrubbed.Contains("somebody"), "the build-machine user name must not reach the client");
        // the exception body is still forwarded, so a client can act on it
        StringAssert.Contains(scrubbed, nameof(InvalidOperationException));
    }

    [TestMethod]
    public void ComposeMessage_OmitsTheExceptionSegment_WhenThereIsNone()
    {
        var composed = ScrubbingLanguageServerLogger.ComposeMessage(
            "Cat", state: "hello", exception: null, formatter: (state, _) => state);

        Assert.AreEqual("Cat: hello | 'hello'", composed);
    }

    [TestMethod]
    [DataRow(LogLevel.Critical, MessageType.Error)]
    [DataRow(LogLevel.Error, MessageType.Error)]
    [DataRow(LogLevel.Warning, MessageType.Warning)]
    [DataRow(LogLevel.Information, MessageType.Info)]
    [DataRow(LogLevel.Debug, MessageType.Log)]
    [DataRow(LogLevel.Trace, MessageType.Log)]
    public void TryGetMessageType_MapsEveryForwardableLevel(LogLevel level, MessageType expected)
    {
        Assert.IsTrue(ScrubbingLanguageServerLogger.TryGetMessageType(level, out var type));
        Assert.AreEqual(expected, type);
    }

    [TestMethod]
    public void TryGetMessageType_DoesNotForwardNone()
        => Assert.IsFalse(ScrubbingLanguageServerLogger.TryGetMessageType(LogLevel.None, out _));
}
