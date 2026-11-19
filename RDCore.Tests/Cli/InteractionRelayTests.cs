using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.Runtime.Support.Host;
using RDCore.External;
using RDCore.External.Interaction;
using RDCore.External.Settings;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Runtime;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Workspace;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using RDCore.SDK.Platform.Components;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Cli;

/// <summary>
/// The dialogs of a program that runs in the environment host travel to the client of the language server - the interactive shell - when it shows dialogs, and are
/// the platform's own when it does not; the settings are the workspace's.
/// </summary>
[TestClass]
public sealed class InteractionRelayTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(5);

    private static readonly MessageBoxRequest YesNo = new("Sure?", "Project1", VBMsgBoxStyle.VBYesNo);

    private static (RelayedUserInteraction Relay, IEnvironmentSessionProvider Provider, IUserInteraction Fallback, RuntimeOutputBuffer Output) Relay(IHostInteractionChannel? channel)
    {
        var provider = Substitute.For<IEnvironmentSessionProvider>();
        var router = new RuntimeOutputRouter();
        var output = new RuntimeOutputBuffer();
        router.Target = output;
        provider.Output.Returns(router);
        provider.Interaction.Returns(channel);
        var fallback = Substitute.For<IUserInteraction>();
        fallback.ShowMessage(Arg.Any<MessageBoxRequest>(), Arg.Any<CancellationToken>()).Returns(VBMsgBoxResult.VBYes);
        fallback.Prompt(Arg.Any<InputBoxRequest>(), Arg.Any<CancellationToken>()).Returns("platform");
        return (new RelayedUserInteraction(provider, fallback), provider, fallback, output);
    }

    [TestMethod]
    public void WithNoConnection_TheDialogIsThePlatforms()
    {
        var (relay, _, fallback, _) = Relay(channel: null);
        Assert.AreEqual(VBMsgBoxResult.VBYes, relay.ShowMessage(YesNo));
        Assert.AreEqual("platform", relay.Prompt(new InputBoxRequest("x", "t")));
        fallback.Received(1).ShowMessage(YesNo, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void AClientThatShowsDialogs_AnswersThem_AndIsShownWhatWasPrintedBefore()
    {
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.MessageBoxAsync(Arg.Any<HostMessageBoxParams>(), Arg.Any<CancellationToken>()).Returns(new MessageBoxResult { Handled = true, Result = (int)VBMsgBoxResult.VBNo });
        var (relay, _, fallback, output) = Relay(channel);
        output.Write("before");
        output.WriteLine();
        output.Write("pending");

        Assert.AreEqual(VBMsgBoxResult.VBNo, relay.ShowMessage(YesNo));

        _ = channel.Received().MessageBoxAsync(Arg.Is<HostMessageBoxParams>(request =>
            request.Prompt == "Sure?" && request.Title == "Project1" && request.Style == (int)VBMsgBoxStyle.VBYesNo && request.Output.SequenceEqual(new[] { "before" })),
            Arg.Any<CancellationToken>());
        CollectionAssert.AreEqual(new[] { "pending" }, output.Lines.ToArray(), "what the client was shown is not said again, and an open line is still the run's");
        fallback.DidNotReceiveWithAnyArgs().ShowMessage(default!, default);
    }

    [TestMethod]
    public void AClientThatDoesNotShowDialogs_LeavesThemToThePlatform_AndWhatWasPrintedToTheRun()
    {
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.MessageBoxAsync(Arg.Any<HostMessageBoxParams>(), Arg.Any<CancellationToken>()).Returns(MessageBoxResult.NotHandled);
        var (relay, _, _, output) = Relay(channel);
        output.Write("before");
        output.WriteLine();

        Assert.AreEqual(VBMsgBoxResult.VBYes, relay.ShowMessage(YesNo));
        CollectionAssert.AreEqual(new[] { "before" }, output.Lines.ToArray());
    }

    // a host the language server started is seen by nobody, so the platform's dialog is never shown after a client that shows them - it was, and the
    // run waited on it forever (HexIDE answering a vbOKOnly box with vbYes).
    [TestMethod]
    public void AnAnswerThatIsNoButtonOfTheDialog_FailsTheRun_AndShowsNoDialogOfThePlatforms()
    {
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.MessageBoxAsync(Arg.Any<HostMessageBoxParams>(), Arg.Any<CancellationToken>()).Returns(new MessageBoxResult { Handled = true, Result = (int)VBMsgBoxResult.VBAbort });
        var (relay, _, fallback, _) = Relay(channel);

        var failed = Assert.ThrowsExactly<DialogNotAnsweredException>(() => relay.ShowMessage(YesNo));

        Assert.AreEqual(VBRuntimeErrorId.UnusableDialogAnswer.ToCode(), failed.Code);
        StringAssert.Contains(failed.Message, "vbYesNo");
        StringAssert.Contains(failed.Message, ((int)VBMsgBoxResult.VBAbort).ToString(System.Globalization.CultureInfo.InvariantCulture));
        fallback.DidNotReceiveWithAnyArgs().ShowMessage(default!, default);
    }

    [TestMethod]
    public void AFailureTheLanguageServerTells_FailsTheRun_WithItsMessage()
    {
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.MessageBoxAsync(Arg.Any<HostMessageBoxParams>(), Arg.Any<CancellationToken>()).Returns(new MessageBoxResult { Handled = true, Result = 6, Failure = "HexIDE answered 6" });

        var failed = Assert.ThrowsExactly<DialogNotAnsweredException>(() => Relay(channel).Relay.ShowMessage(new MessageBoxRequest("x", "t", VBMsgBoxStyle.VBOKOnly)));

        Assert.AreEqual(VBRuntimeErrorId.UnusableDialogAnswer.ToCode(), failed.Code);
        Assert.AreEqual("HexIDE answered 6", failed.Message);
    }

    [TestMethod]
    public void ADialogTheClientDeclined_FailsTheRun_ForNobodyToAnswer()
    {
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.MessageBoxAsync(Arg.Any<HostMessageBoxParams>(), Arg.Any<CancellationToken>()).Returns(new MessageBoxResult { Declined = true });
        channel.InputBoxAsync(Arg.Any<HostInputBoxParams>(), Arg.Any<CancellationToken>()).Returns(new InputBoxResult { Declined = true });
        var (relay, _, fallback, _) = Relay(channel);

        Assert.AreEqual(VBRuntimeErrorId.NobodyToAnswer.ToCode(), Assert.ThrowsExactly<DialogNotAnsweredException>(() => relay.ShowMessage(YesNo)).Code);
        Assert.AreEqual(VBRuntimeErrorId.NobodyToAnswer.ToCode(), Assert.ThrowsExactly<DialogNotAnsweredException>(() => relay.Prompt(new InputBoxRequest("x", "t"))).Code);
        fallback.DidNotReceiveWithAnyArgs().ShowMessage(default!, default);
        fallback.DidNotReceiveWithAnyArgs().Prompt(default!, default);
    }

    [TestMethod]
    public void AConnectionThatFails_FailsTheRun_AndShowsNoDialogOfThePlatforms()
    {
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.InputBoxAsync(Arg.Any<HostInputBoxParams>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("lost"));
        var (relay, _, fallback, _) = Relay(channel);

        var failed = Assert.ThrowsExactly<DialogNotAnsweredException>(() => relay.Prompt(new InputBoxRequest("x", "t")));

        Assert.AreEqual(VBRuntimeErrorId.UnusableDialogAnswer.ToCode(), failed.Code);
        StringAssert.Contains(failed.Message, "lost");
        fallback.DidNotReceiveWithAnyArgs().Prompt(default!, default);
    }

    [TestMethod]
    public void AnInputBoxTheClientCancelled_IsCancelled_AndOneConfirmedEmptyIsEmpty()
    {
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.InputBoxAsync(Arg.Any<HostInputBoxParams>(), Arg.Any<CancellationToken>()).Returns(
            new InputBoxResult { Handled = true, Cancelled = true },
            new InputBoxResult { Handled = true, Value = string.Empty });
        var (relay, _, _, _) = Relay(channel);
        var request = new InputBoxRequest("Name?", "t", "you", 10, 20);

        Assert.IsNull(relay.Prompt(request));
        Assert.AreEqual(string.Empty, relay.Prompt(request));
        _ = channel.Received().InputBoxAsync(Arg.Is<HostInputBoxParams>(sent => sent.Default == "you" && sent.XPos == 10 && sent.YPos == 20), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void ARunThatIsStopped_StopsWaitingForTheClient()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.MessageBoxAsync(Arg.Any<HostMessageBoxParams>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException(stop.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => Relay(channel).Relay.ShowMessage(YesNo, stop.Token));
    }

    // ---- the session's outside world ----

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-relay-ws");

    private static EnvironmentSessionProvider Compose(bool legacyRegistrySettings = false)
    {
        var project = new ProjectFile(Root, new RDCoreProject { Name = "Ducks", Modules = [] });
        var files = new MockFileSystem(new Dictionary<string, MockFileData> { [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)) });
        var outside = ExternalWorld.None with { Interaction = Substitute.For<IUserInteraction>() };
        var provider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, LegacyRegistrySettings: legacyRegistrySettings), files, NullLogger<EnvironmentSessionProvider>.Instance, outside: outside);
        provider.Compose(project.ProjectInfo, new Uri(Root));
        return provider;
    }

    [TestMethod]
    public void TheSessionsWorld_IsTitledAfterItsProject_AndItsDialogsAreRelayed()
    {
        var provider = Compose();
        Assert.AreEqual("Ducks", provider.Outside.DialogTitle);
        Assert.IsInstanceOfType<RelayedUserInteraction>(provider.Outside.Interaction);
    }

    [TestMethod]
    public void TheSessionsSettings_AreItsWorkspacesAppSettings()
    {
        var settings = (JsonWorkspaceSettings)Compose().Outside.WorkspaceSettings;
        Assert.AreEqual(Path.Combine(Root, "appsettings.json"), settings.PathOf(null));
    }

    [TestMethod]
    public void TheLegacySettings_AreTheWorkspaces_UnlessTheRegistryIsAskedForOnWindows()
    {
        Assert.IsInstanceOfType<JsonProgramSettings>(Compose().Outside.ProgramSettings);
        var asked = Compose(legacyRegistrySettings: true).Outside.ProgramSettings;
        Assert.AreEqual(OperatingSystem.IsWindows() ? "RegistryProgramSettings" : nameof(JsonProgramSettings), asked.GetType().Name);
    }

    [TestMethod]
    public void TheKnob_IsBoundFromAppSettings()
    {
        Assert.IsFalse(RuntimeEnvironmentProfile.From(new SDK.Server.Configuration.SdkEnvironmentOptions()).LegacyRegistrySettings);
        Assert.IsTrue(RuntimeEnvironmentProfile.From(new SDK.Server.Configuration.SdkEnvironmentOptions { LegacyRegistrySettings = true }).LegacyRegistrySettings);
    }

    // ---- the language server ----

    private static IInteractionClient Client(bool showsDialogs)
    {
        var client = Substitute.For<IInteractionClient>();
        client.ShowsDialogs.Returns(showsDialogs);
        client.MessageBoxAsync(Arg.Any<SessionMessageBoxRequest>(), Arg.Any<CancellationToken>()).Returns(new MessageBoxResult { Handled = true, Result = 7 });
        client.InputBoxAsync(Arg.Any<SessionInputBoxRequest>(), Arg.Any<CancellationToken>()).Returns(new InputBoxResult { Handled = true, Value = "typed" });
        return client;
    }

    // the relay the language server is made of and the gate its diagnostics pulls are declined by are one: a pull while the relay's dialog is open is declined.
    [TestMethod]
    public async Task TheLanguageServer_DeclinesAPull_WhileADialogItsRelayAsked_IsOpen()
    {
        using var provider = new ServiceCollection().AddPlatformCoordination(new Version(1, 0)).BuildServiceProvider();
        var relay = provider.GetRequiredService<IClientInteractionRelay>();
        var dialogs = provider.GetRequiredService<OpenDialogs>();

        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<MessageBoxResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Client(showsDialogs: true);
        client.MessageBoxAsync(Arg.Any<SessionMessageBoxRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            asked.SetResult();
            return answer.Task;
        });
        relay.Attach(client);

        var dialog = relay.MessageBoxAsync(new HostMessageBoxParams { Prompt = "p", Style = (int)VBMsgBoxStyle.VBOKOnly }, CancellationToken.None);
        await asked.Task.WaitAsync(SafetyCap);

        var declined = await Assert.ThrowsExactlyAsync<RpcErrorException>(() => dialogs.UnlessADialogIsOpenAsync(CancellationToken.None, _ => Task.FromResult(0)));
        Assert.AreEqual(ErrorCodes.ServerCancelled, declined.Code);

        answer.SetResult(new MessageBoxResult { Handled = true, Result = 1 });
        Assert.AreEqual(1, (await dialog.WaitAsync(SafetyCap)).Result);
        Assert.AreEqual(2, await dialogs.UnlessADialogIsOpenAsync(CancellationToken.None, _ => Task.FromResult(2)));
    }

    [TestMethod]
    public async Task TheLanguageServer_RelaysADialog_ToAClientThatShowsThem()
    {
        var relay = new ClientInteractionRelay(new OpenDialogs());
        var client = Client(showsDialogs: true);
        relay.Attach(client);

        var answer = await new HostMessageBoxHandler(relay).Handle(new HostMessageBoxParams { Prompt = "p", Title = "t", Style = 4, Output = ["line"] }, CancellationToken.None);
        var typed = await new HostInputBoxHandler(relay).Handle(new HostInputBoxParams { Prompt = "p", Default = "d", XPos = 1 }, CancellationToken.None);

        Assert.AreEqual(7, answer.Result);
        Assert.AreEqual("typed", typed.Value);
        await client.Received().MessageBoxAsync(Arg.Is<SessionMessageBoxRequest>(sent => sent.Prompt == "p" && sent.Title == "t" && sent.Style == 4 && sent.Output.Single() == "line"), Arg.Any<CancellationToken>());
        await client.Received().InputBoxAsync(Arg.Is<SessionInputBoxRequest>(sent => sent.Default == "d" && sent.XPos == 1), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task TheLanguageServer_AnswersThatNobodyWasAsked_WhenItsClientDoesNotShowDialogs_OrThereIsNone()
    {
        var relay = new ClientInteractionRelay(new OpenDialogs());
        Assert.IsFalse((await relay.MessageBoxAsync(new HostMessageBoxParams(), CancellationToken.None)).Handled);

        var client = Client(showsDialogs: false);
        relay.Attach(client);
        Assert.IsFalse((await relay.MessageBoxAsync(new HostMessageBoxParams(), CancellationToken.None)).Handled);
        Assert.IsFalse((await relay.InputBoxAsync(new HostInputBoxParams(), CancellationToken.None)).Handled);
        await client.DidNotReceiveWithAnyArgs().MessageBoxAsync(default!, default);
    }

    // what HexIDE's probe did - a vbOKOnly box answered with vbYes - is told to the host as the failure it is, naming the client.
    [TestMethod]
    public async Task TheLanguageServer_TellsAnAnswerThatIsNoButtonOfTheDialog_NamingTheClient()
    {
        var relay = new ClientInteractionRelay(new OpenDialogs());
        var client = Client(showsDialogs: true);
        client.Name.Returns("HexIDE");
        client.MessageBoxAsync(Arg.Any<SessionMessageBoxRequest>(), Arg.Any<CancellationToken>()).Returns(new MessageBoxResult { Handled = true, Result = 6 });
        relay.Attach(client);

        var answer = await relay.MessageBoxAsync(new HostMessageBoxParams { Prompt = "p", Style = (int)VBMsgBoxStyle.VBOKOnly }, CancellationToken.None);

        Assert.IsNotNull(answer.Failure);
        StringAssert.Contains(answer.Failure, "HexIDE");
        StringAssert.Contains(answer.Failure, "vbOKOnly");
        StringAssert.Contains(answer.Failure, "6");
    }

    [TestMethod]
    public async Task TheLanguageServer_TellsADialogAClientThatShowsThemDidNotHandle_AsDeclined()
    {
        var relay = new ClientInteractionRelay(new OpenDialogs());
        var client = Client(showsDialogs: true);
        client.MessageBoxAsync(Arg.Any<SessionMessageBoxRequest>(), Arg.Any<CancellationToken>()).Returns(MessageBoxResult.NotHandled);
        client.InputBoxAsync(Arg.Any<SessionInputBoxRequest>(), Arg.Any<CancellationToken>()).Returns(InputBoxResult.NotHandled);
        relay.Attach(client);

        Assert.IsTrue((await relay.MessageBoxAsync(new HostMessageBoxParams(), CancellationToken.None)).Declined);
        Assert.IsTrue((await relay.InputBoxAsync(new HostInputBoxParams(), CancellationToken.None)).Declined);
    }
}
