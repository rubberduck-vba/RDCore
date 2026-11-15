using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Commands;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The codes whose diagnostics are not reported (<c>Analysis.DisabledDiagnostics</c>): the user's and the workspace's together, which a change the client says
/// changes, and the workspace's findings with them. The code action that disables a code asks the client to add it to the user's list.
/// </summary>
[TestClass]
public sealed class DisabledDiagnosticsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-disabled-ws");

    private readonly SdkAppOptions _options = new();
    private readonly ILiveSettings _live = Substitute.For<ILiveSettings>();
    private readonly WorkspaceRefresh _refresh = new(TimeSpan.Zero);

    private DisabledDiagnostics Sut(IWorkspaceSettings? workspace = null) => new(Options.Create(_options), _live, _refresh, () => Root, workspace);

    private static string[] Of(IEnumerable<SDK.Extensibility.Diagnostics.RDCoreDiagnosticCode> codes) => [.. codes.Select(code => code.ToString()).Order()];

    [TestMethod]
    public void TheCodesTheUserDisabled_AndThoseTheWorkspaceDisabled_AreDisabledTogether()
    {
        _options.Analysis.DisabledDiagnostics = ["rdc01001"];
        var workspace = Substitute.For<IWorkspaceSettings>();
        workspace.Read(Arg.Any<Uri>(), "Analysis.DisabledDiagnostics").Returns(["RDC01002", "RDC01001"]);

        using var disabled = Sut(workspace);

        CollectionAssert.AreEqual(new[] { "RDC01001", "RDC01002" }, Of(disabled.Codes));
    }

    [TestMethod]
    public void ACodeOfTheLanguagesOwnErrors_IsNeverDisabled_EvenWhenAFileSaysSo()
    {
        _options.Analysis.DisabledDiagnostics = ["VBC00013", "VBR00013", "RDC01001"];

        using var disabled = Sut();

        CollectionAssert.AreEqual(new[] { "RDC01001" }, Of(disabled.Codes));
    }

    [TestMethod]
    public void AChangeOfTheCodes_RefreshesTheWorkspacesFindings_AndAChangeOfNothingElseDoesNot()
    {
        using var disabled = Sut();
        var generation = _refresh.Generation;

        _live.Changed += Raise.Event();
        Assert.AreEqual(generation, _refresh.Generation, "another setting changed");

        _options.Analysis.DisabledDiagnostics = ["RDC01001"];
        _live.Changed += Raise.Event();
        Assert.AreEqual(generation + 1, _refresh.Generation);
        CollectionAssert.AreEqual(new[] { "RDC01001" }, Of(disabled.Codes));

        _options.Analysis.DisabledDiagnostics = [];
        _live.Changed += Raise.Event();
        Assert.AreEqual(generation + 2, _refresh.Generation, "a code enabled again");
        Assert.IsEmpty(disabled.Codes);
    }

    private static IPlatformClientCapabilitiesService Expecting(bool listSettingValue)
    {
        var capabilities = new PlatformClientCapabilitiesService();
        capabilities.Record(new PlatformInitializeParams
        {
            Expected = new CorePlatformClientCapabilities { LanguageServer = new LanguageServerCapabilities { ListSettingValue = new ListSettingValue(listSettingValue) } },
        });
        return capabilities;
    }

    private static readonly ListSettingValueParams Disabling = new()
    {
        Key = "Analysis.DisabledDiagnostics", Value = "RDC01002", Scope = SettingScope.Platform, Document = new Uri("file:///c:/ws/Module1.bas"),
    };

    [TestMethod]
    public async Task TheCommandOfTheActionThatDisablesACode_AsksTheClientToAddItToTheUsersList()
    {
        var server = Substitute.For<ILanguageServerFacade>();
        server.SendRequest(Arg.Any<ListSettingValueRequest>(), Arg.Any<CancellationToken>()).Returns(new ListSettingValueResult { Applied = true });

        var answer = await new ListSettingValueCommand(server, Expecting(true))
            .ExecuteAsync(ListSettingValueCommand.ArgumentsOf(Disabling), Substitute.For<IServerCommandContext>(), CancellationToken.None);

        Assert.IsTrue((bool?)answer!["applied"]);
        await server.Received(1).SendRequest(
            Arg.Is<ListSettingValueRequest>(request => request.Key == Disabling.Key && request.Value == "RDC01002" && request.Scope == SettingScope.Platform && !request.Remove
                && request.Document == Disabling.Document),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task TheCommandOfTheActionThatDisablesACode_IsRefused_ByAClientThatDoesNotTakeTheChange()
    {
        var server = Substitute.For<ILanguageServerFacade>();

        var refused = await Assert.ThrowsExactlyAsync<ServerCommandRefusedException>(() => new ListSettingValueCommand(server, Expecting(false))
            .ExecuteAsync(ListSettingValueCommand.ArgumentsOf(Disabling), Substitute.For<IServerCommandContext>(), CancellationToken.None));

        Assert.AreEqual(RefactoringTitles.SettingListNotTaken(), refused.Message);
        await server.DidNotReceiveWithAnyArgs().SendRequest(default(ListSettingValueRequest)!, default);
    }
}
