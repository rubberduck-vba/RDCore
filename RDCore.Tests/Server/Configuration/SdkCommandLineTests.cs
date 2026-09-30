using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.Server.Configuration;

/// <summary>
/// <see cref="SdkCommandLine"/> — telling a command line that asked a question apart from one that
/// configures a run.
/// </summary>
/// <remarks>
/// The parser answers <c>--help</c> and <c>--version</c> by writing the text itself, then reports the
/// request as a parse error and hands back a default instance. Reading that instance as configuration
/// is what made <c>rdc --help</c> print its help, then an <c>ArgumentNullException</c> stack trace
/// underneath it from the "a client cannot start without a workspace" guard, and exit -1.
/// </remarks>
[TestClass]
public sealed class SdkCommandLineTests
{
    [TestMethod]
    [DataRow("--help")]
    [DataRow("--version")]
    public void ATextOnlyRequest_IsAnswered(string arg)
        => Assert.IsTrue(SdkCommandLine.TryAnswerTextOnlyRequest([arg]));

    [TestMethod]
    public void OrdinaryArguments_AreNotATextOnlyRequest()
        => Assert.IsFalse(SdkCommandLine.TryAnswerTextOnlyRequest(["--workspace", "C:/ws"]));

    [TestMethod]
    public void NoArguments_AreNotATextOnlyRequest()
        // the interactive shell's own command line: nothing at all is not a question.
        => Assert.IsFalse(SdkCommandLine.TryAnswerTextOnlyRequest([]));

    [TestMethod]
    public void OrdinaryArguments_Parse()
        => Assert.AreEqual("C:/ws", SdkCommandLine.Parse(["--workspace", "C:/ws"]).WorkspaceUri);
}
