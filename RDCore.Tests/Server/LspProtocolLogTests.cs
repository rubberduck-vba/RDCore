using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Server.Logging;

namespace RDCore.Tests.Server;

/// <summary>
/// Shared shaping for the two client-forwarding logger paths — message layout, level mapping, and
/// the <c>window/logMessage</c> vs <c>$/logTrace</c> routing policy.
/// </summary>
[TestClass]
public sealed class LspProtocolLogTests
{
    [TestMethod]
    // OmniSharp's protocol logger appends exception.ToString() verbatim; every scrub mode must strip
    // the build-machine path before the composed message reaches the client.
    [DataRow(SourcePathScrubMode.RepoRelative, "RDCore.Parsing/ModuleParser.cs:line 51")]
    [DataRow(SourcePathScrubMode.FileName, "ModuleParser.cs:line 51")]
    [DataRow(SourcePathScrubMode.Redacted, "<source>:line 51")]
    public void ComposedMessage_IsScrubbedPerMode(SourcePathScrubMode mode, string expectedFragment)
    {
        var fault = new InvalidOperationException(
            "boom\r\n" +
            "   at RDCore.Parsing.ModuleParser.Parse() in C:\\Users\\somebody\\src\\RDCore\\RDCore.Parsing\\ModuleParser.cs:line 51");

        var composed = LspProtocolLog.ComposeMessage(
            "RDCore.Parsing.ModuleParser", state: "parse failed", exception: fault, formatter: (state, _) => state);
        var scrubbed = SourcePathAnonymizer.Scrub(composed, mode);

        StringAssert.Contains(scrubbed, expectedFragment);
        Assert.IsFalse(scrubbed.Contains("C:\\"), "the drive-letter prefix must not reach the client");
        Assert.IsFalse(scrubbed.Contains("somebody"), "the build-machine user name must not reach the client");
        StringAssert.Contains(scrubbed, nameof(InvalidOperationException));
    }

    [TestMethod]
    public void ComposeMessage_OmitsTheExceptionSegment_WhenThereIsNone()
    {
        var composed = LspProtocolLog.ComposeMessage(
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
        Assert.IsTrue(LspProtocolLog.TryGetMessageType(level, out var type));
        Assert.AreEqual(expected, type);
    }

    [TestMethod]
    public void TryGetMessageType_DoesNotForwardNone()
        => Assert.IsFalse(LspProtocolLog.TryGetMessageType(LogLevel.None, out _));

    [TestMethod]
    // (level, client $/setTrace) -> (window/logMessage, $/logTrace, verbose)
    [DataRow(LogLevel.Error, InitializeTrace.Off, true, false, false)]
    [DataRow(LogLevel.Warning, InitializeTrace.Verbose, true, false, true)]      // warnings never ride the trace channel
    [DataRow(LogLevel.Information, InitializeTrace.Off, false, false, false)]     // narration is silent with trace off
    [DataRow(LogLevel.Information, InitializeTrace.Messages, false, true, false)] // narration, no detail
    [DataRow(LogLevel.Debug, InitializeTrace.Verbose, false, true, true)]        // narration + detail
    [DataRow(LogLevel.None, InitializeTrace.Verbose, false, false, true)]        // nothing forwarded
    public void Route_PicksTheChannelsByLevelAndTraceState(
        LogLevel level, InitializeTrace trace, bool logMessage, bool logTrace, bool includeVerbose)
    {
        var route = LspProtocolLog.Route(level, trace);

        Assert.AreEqual(logMessage, route.LogMessage);
        Assert.AreEqual(logTrace, route.LogTrace);
        Assert.AreEqual(includeVerbose, route.IncludeVerbose);
    }
}
