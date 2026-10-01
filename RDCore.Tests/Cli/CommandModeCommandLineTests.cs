using RDCore.CLI;
using RDCore.SDK.Server;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdc.exe &lt;verb&gt; …</c>: command mode. Its command line is a verb and the verb's own options, which is not the
/// platform's command line, so the platform's parser has no business answering it.
/// </summary>
/// <remarks>
/// Every application used to have its arguments read as the platform's before anything else happened, and an option it
/// did not know was a mistake. For a verb that made every option of its own one: <c>describe-ext … --description x</c>
/// failed with "Option 'description' is unknown", and so did the publish script that calls it.
/// </remarks>
[TestClass]
public sealed class CommandModeCommandLineTests
{
    // the real hosts, asked through the base type that declares the question.
    private static AppHost<RDCoreConsoleCommandApp> CommandMode() => new RDCoreConsoleCommandHost();

    private static AppHost<RDCoreConsoleClientApp> ClientMode() => new RDCoreConsoleClientHost();

    [TestMethod]
    public void AVerbsOwnOptions_AreNotAnsweredByThePlatformsParser()
    {
        string[] args = ["describe-ext", "RDCore.Diagnostics.exe", "--description", "x", "--overwrite", "--unsafe-dev-mode"];

        Assert.IsFalse(CommandMode().TryAnswerCommandLine(args, out _));
    }

    [TestMethod]
    public void ACommandModeCommandLine_IsNotAnsweredWhateverItContains()
        // what is wrong with it is for the verb to say.
        => Assert.IsFalse(CommandMode().TryAnswerCommandLine(["no-such-verb", "--nonsense"], out _));

    [TestMethod]
    public void AClientsUnknownOption_IsStillAMistake()
    {
        // the contrast: a client is started with the platform's command line, and an option it does not have is
        // wrong - which `rdc --nonsense` reporting itself, rather than failing later, depends on.
        Assert.IsTrue(ClientMode().TryAnswerCommandLine(["--nonsense"], out var exitCode));
        Assert.AreNotEqual(0, exitCode);
    }

    [TestMethod]
    public void AClientsHelpRequest_IsStillAnsweredSuccessfully()
    {
        Assert.IsTrue(ClientMode().TryAnswerCommandLine(["--help"], out var exitCode));
        Assert.AreEqual(0, exitCode);
    }
}
