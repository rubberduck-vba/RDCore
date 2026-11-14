using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI.App.Repl;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;
using System.Text.Json;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// What the shell asks the platform to run: its program is its own module, of the session, which the project of a workspace the shell is attached to does not
/// list (<see cref="SessionExecuteParams.SessionModule"/>); a module of any other name is the project's. It asks for the code actions it can take, and no
/// others: the fixes of diagnostics and the renames.
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

    [TestMethod]
    public async Task TheShell_AsksForTheCodeActionsItCanTake_TheFixesAndTheRenames()
    {
        var client = Substitute.For<IRDCoreClientApp>();
        CodeActionParams? sent = null;
        client.SendRequestAsync<CodeActionParams, CommandOrCodeActionContainer?>(Arg.Do<CodeActionParams>(request => sent = request), Arg.Any<CancellationToken>())
            .Returns((CommandOrCodeActionContainer?)null);
        var sut = new ReplPlatformClient(client);

        _ = await sut.GetCodeActionsAsync(new Uri("file:///Main.rdc"), new Range(0, 0, 1, 0), [], CancellationToken.None);

        Assert.IsNotNull(sent);
        CollectionAssert.AreEqual(new[] { "quickfix", RefactoringKinds.Rename }, sent.Context.Only!.Select(kind => kind.ToString()).ToArray());
    }
}
