using NSubstitute;
using RDCore.CLI.App.Repl;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using System.Text.Json;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// What the shell asks the platform to run: its program is its own module, of the session, which the project of a workspace the shell is attached to does not
/// list (<see cref="SessionExecuteParams.SessionModule"/>); a module of any other name is the project's.
/// </summary>
[TestClass]
public sealed class ReplPlatformClientTests
{
    [TestMethod]
    [DataRow(ReplProgram.ModuleName, false, true, DisplayName = "the program, run")]
    [DataRow(ReplProgram.ModuleName, true, true, DisplayName = "the program, run under a debugger")]
    [DataRow(ReplProgram.ImmediateModuleName, false, true, DisplayName = "a line, run in a workspace")]
    [DataRow(ReplProgram.ImmediateModuleName, true, true, DisplayName = "a line, run in a workspace under a debugger")]
    [DataRow("Module1", false, false, DisplayName = "a module of the project, run")]
    [DataRow("Module1", true, false, DisplayName = "a module of the project, run under a debugger")]
    public async Task WhatTheShellRuns_IsASessionModule_OnlyWhenItIsItsProgram(string moduleName, bool debug, bool expected)
    {
        var channel = Substitute.For<IRDCorePlatformChannel>();
        JsonElement? sent = null;
        channel.SendRequestAsync(RDCorePlatformProtocol.SessionExecute, Arg.Do<JsonElement?>(parameters => sent = parameters), Arg.Any<CancellationToken>())
            .Returns(JsonDocument.Parse("{}").RootElement);
        var client = Substitute.For<IRDCoreClientApp>();
        client.Channel.Returns(channel);
        var sut = new ReplPlatformClient(client);

        _ = debug
            ? await sut.ExecuteAsync("Public Sub Main()\r\nEnd Sub\r\n", moduleName, "Main", debug: true, immediate: false, CancellationToken.None)
            : await sut.ExecuteAsync("Public Sub Main()\r\nEnd Sub\r\n", moduleName, "Main", CancellationToken.None);

        Assert.IsNotNull(sent);
        Assert.AreEqual(expected, sent.Value.GetProperty("sessionModule").GetBoolean(), sent.Value.ToString());
    }
}
