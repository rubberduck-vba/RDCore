using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Debugging;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What is pushed to a client again when what a program that waits does not have changes, or when the program begins or ends waiting in break mode: the
/// findings of the modules the edits name, and of every module at a break, which withdraws and brings back what comes of evaluating the code.
/// </summary>
[TestClass]
public sealed class EditStatusPublicationTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-edit-status-publication");
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly IEditStatusMonitor _monitor = Substitute.For<IEditStatusMonitor>();
    private readonly IDocumentDiagnosticsService _diagnostics = Substitute.For<IDocumentDiagnosticsService>();
    private readonly IDiagnosticsPublisher _publisher = Substitute.For<IDiagnosticsPublisher>();
    private readonly List<Uri> _published = [];
    private TaskCompletionSource _allPublished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _expected;

    private static readonly WorkspaceDocument Module1 = new("Module1.bas", Root, "Attribute VB_Name = \"Module1\"\r\n");
    private static readonly WorkspaceDocument Module2 = new("Module2.bas", Root, "Attribute VB_Name = \"Module2\"\r\n");

    public EditStatusPublicationTests()
    {
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.GetAllDocuments().Returns([Module1, Module2]);
        _diagnostics.GetAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<SDK.Server.Progress.WorkProgressStep>?>())
            .Returns(call => DocumentDiagnosticsResult.Fresh(1, []));
        _publisher.IsEnabled.Returns(true);
        _publisher.When(publisher => publisher.Publish(Arg.Any<Uri>(), Arg.Any<int?>(), Arg.Any<IReadOnlyList<Diagnostic>>())).Do(call =>
        {
            lock (_published)
            {
                _published.Add(call.Arg<Uri>());
                if (_published.Count == _expected)
                {
                    _allPublished.TrySetResult();
                }
            }
        });

        new EditStatusPublication(_monitor, documents, Substitute.For<IParsingClientService>(), _diagnostics, _publisher, NullLogger<EditStatusPublication>.Instance).Attach();
    }

    private async Task<Uri[]> PublishedAsync(HostDebugEditStatusResult previous, HostDebugEditStatusResult current, int expected)
    {
        lock (_published)
        {
            _published.Clear();
            _expected = expected;
            _allPublished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _monitor.Changed += Raise.Event<Action<HostDebugEditStatusResult, HostDebugEditStatusResult>>(previous, current);
        await _allPublished.Task.WaitAsync(Patience);
        lock (_published)
        {
            return [.. _published];
        }
    }

    [TestMethod]
    [DataRow(false, true, DisplayName = "break mode begun")]
    [DataRow(true, false, DisplayName = "break mode ended")]
    public async Task BreakModeBegunOrEnded_PushesEveryModuleAgain(bool before, bool after)
    {
        var published = await PublishedAsync(new HostDebugEditStatusResult { Suspended = before }, new HostDebugEditStatusResult { Suspended = after }, expected: 2);

        CollectionAssert.AreEquivalent(new[] { Module1.Id.Uri.ToUri(), Module2.Id.Uri.ToUri() }, published);
    }

    // an edit listed while the program waits as it did is of the modules it names, and of no other.
    [TestMethod]
    public async Task AnEditListed_WhileTheProgramWaitsStill_PushesTheModuleItNamesAgain()
    {
        var edit = new HostDebugEdit { Name = "Module2.Foo", Kind = HostDebugEditKind.Restart, Reason = "a local was removed", ModuleUri = new Uri("file:///ws#Module2") };

        var published = await PublishedAsync(new HostDebugEditStatusResult { Suspended = true }, new HostDebugEditStatusResult { Suspended = true, Edits = [edit] }, expected: 1);

        CollectionAssert.AreEqual(new[] { Module2.Id.Uri.ToUri() }, published);
    }
}
