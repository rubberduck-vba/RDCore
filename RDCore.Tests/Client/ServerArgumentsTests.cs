using Microsoft.Extensions.Logging;
using RDCore.SDK.Client;
using RDCore.SDK.Model;
using RDCore.SDK.Server.Configuration;
using System.Text.RegularExpressions;

namespace RDCore.Tests.Client;

/// <summary>
/// The command line a client starts a server process with. It is read back by the server's own argument parser, which
/// is the only thing that can say whether it is a command line a server starts on: one that does not parse is a server
/// that exits before it ever connects, and the client only finds that out as "the child process exited".
/// </summary>
[TestClass]
public sealed partial class ServerArgumentsTests
{
    // a command line as the process would be handed it: split on spaces, a quoted run being one argument.
    [GeneratedRegex("\"[^\"]*\"|\\S+")]
    private static partial Regex Token();

    private static string[] Split(string commandLine)
        => [.. Token().Matches(commandLine).Select(match => match.Value.Trim('"'))];

    private static SdkAppCommandLineArgs ParsedBack(ImplicitDeclarationScope scope, bool verbose = true)
    {
        var arguments = Split(RDCoreServerProcess.ServerArguments(1234, "pipe", "C:/ws", LogLevel.Trace, verbose, scope));

        Assert.IsFalse(SdkCommandLine.TryAnswer(arguments, out var exitCode), $"the server's parser rejected it ({exitCode}): {string.Join(" ", arguments)}");
        return SdkCommandLine.Parse(arguments);
    }

    [TestMethod]
    [DataRow(ImplicitDeclarationScope.Procedure)]
    [DataRow(ImplicitDeclarationScope.Module)]
    public void TheCommandLine_IsOneTheServerCanStartOn(ImplicitDeclarationScope scope)
    {
        var parsed = ParsedBack(scope);

        Assert.AreEqual(1234, parsed.ClientProcessId);
        Assert.AreEqual("C:/ws", parsed.WorkspaceUri);
    }

    [TestMethod]
    public void AShellsScope_ReachesTheServer()
        => Assert.AreEqual(ImplicitDeclarationScope.Module, ParsedBack(ImplicitDeclarationScope.Module).ImplicitDeclarationScope);

    [TestMethod]
    public void TheDefaultScope_IsLeftOutSoTheServersOwnSettingsWin()
        => Assert.IsNull(ParsedBack(ImplicitDeclarationScope.Procedure).ImplicitDeclarationScope);

    [TestMethod]
    public void TheVerboseSwitch_IsTheLastArgument()
        // a switch the parser only reads as one at the end of a command line.
        => StringAssert.EndsWith(
            RDCoreServerProcess.ServerArguments(1, "pipe", "C:/ws", LogLevel.Trace, verbose: true, ImplicitDeclarationScope.Module).TrimEnd(), "-v");
}
