using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.Server.Configuration;

/// <summary>
/// <see cref="SdkCommandLine"/> — telling a command line that has to be answered apart from one that
/// configures a run.
/// </summary>
/// <remarks>
/// The parser answers <c>--help</c> and <c>--version</c> by writing the text itself, and reports what it
/// did not understand the same way; either way it reports a parse error and hands back a default
/// instance. Reading that instance as configuration is what made both <c>rdc --help</c> and
/// <c>rdc --nonsense</c> print a <c>NullReferenceException</c> stack trace underneath the parser's own
/// output, from the "a client cannot start without a workspace" guard.
/// </remarks>
[TestClass]
public sealed class SdkCommandLineTests
{
    [TestMethod]
    [DataRow("--help")]
    [DataRow("--version")]
    public void AQuestion_IsAnsweredSuccessfully(string arg)
    {
        Assert.IsTrue(SdkCommandLine.TryAnswer([arg], out var exitCode));
        Assert.AreEqual(0, exitCode);
    }

    [TestMethod]
    [DataRow("--nonsense", DisplayName = "an unknown option")]
    [DataRow("--connect-timeout=banana", DisplayName = "an option whose value does not convert")]
    public void ACommandLineThatDoesNotParse_IsAnsweredAsAFailure(string arg)
    {
        Assert.IsTrue(SdkCommandLine.TryAnswer([arg], out var exitCode));
        Assert.AreNotEqual(0, exitCode);
    }

    [TestMethod]
    public void OrdinaryArguments_AreNotAnswered()
        => Assert.IsFalse(SdkCommandLine.TryAnswer(["--workspace", "C:/ws"], out _));

    [TestMethod]
    public void NoArguments_AreNotAnswered()
        // the interactive shell's own command line: nothing at all is not a question.
        => Assert.IsFalse(SdkCommandLine.TryAnswer([], out _));

    [TestMethod]
    public void OrdinaryArguments_Parse()
        => Assert.AreEqual("C:/ws", SdkCommandLine.Parse(["--workspace", "C:/ws"]).WorkspaceUri);
}
