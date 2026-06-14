using Microsoft.Extensions.Logging;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace.States;

namespace RDCore.Tests.Workspace;

[TestClass]
public sealed class DocumentStateProviderTests
{
    private static readonly Uri TestWorkspaceUri = new("file://c:/dev/rdcore/workspaces/rdcore.tests");
    private static readonly Uri TestDocumentUri1 = new(TestWorkspaceUri, "/README.md");
    private static readonly Uri TestDocumentUri2 = new(TestWorkspaceUri, "/LICENSE.md");

    private static readonly ILogger<DocumentStateProvider> _logger = Substitute.For<ILogger<DocumentStateProvider>>();

    [TestMethod]
    public void Uninitialized_GetCurrentState_Throws()
    {
        // arrange
        var id = new TextDocumentIdentifier(TestDocumentUri1);
        var sut = new DocumentStateProvider(_logger);

        // act/assert
        Assert.Throws<KeyNotFoundException>(() => sut.GetCurrentState(id));
    }

    [TestMethod]
    public void UninitializedId_GetCurrentState_Throws()
    {
        // arrange
        var id1 = new TextDocumentIdentifier(TestDocumentUri1);
        var id2 = new TextDocumentIdentifier(TestDocumentUri2);
        var sut = new DocumentStateProvider(_logger);

        sut.Initialize([id1]);

        // act/assert
        Assert.Throws<KeyNotFoundException>(() => sut.GetCurrentState(id2));
    }

    [TestMethod]
    public void Initialized_GetCurrentState_Unloaded()
    {
        // arrange
        var id = new TextDocumentIdentifier(TestDocumentUri1);
        var sut = new DocumentStateProvider(_logger);

        // act
        sut.Initialize([id]);
        var result = sut.GetCurrentState(id);

        // assert
        Assert.IsInstanceOfType<UnloadedDocumentState>(result);
    }

    [TestMethod]
    [DataRow(default, default)]
    [DataRow(DocumentStateValue.Unloaded, default)]
    [DataRow(DocumentStateValue.Loaded, DocumentStateValue.Unloaded)]
    [DataRow(DocumentStateValue.Missing, default)]
    [DataRow(DocumentStateValue.LoadError, default)]
    [DataRow(DocumentStateValue.Opened, default)]
    public void OnDocumentUnloaded(DocumentStateValue? initialState, DocumentStateValue? expectedState)
    {
        var id = new TextDocumentIdentifier(TestDocumentUri1);
        TestDocumentStateTransition(sut => sut.OnDocumentUnloaded(id), initialState, expectedState);
    }

    [TestMethod]
    [DataRow(default, default)]
    [DataRow(DocumentStateValue.Unloaded, DocumentStateValue.Loaded)]
    [DataRow(DocumentStateValue.Loaded, default)]
    [DataRow(DocumentStateValue.Missing, default)]
    [DataRow(DocumentStateValue.LoadError, default)]
    [DataRow(DocumentStateValue.Opened, default)]
    public void OnDocumentLoaded(DocumentStateValue? initialState, DocumentStateValue? expectedState)
    {
        var id = new TextDocumentIdentifier(TestDocumentUri1);
        TestDocumentStateTransition(sut => sut.OnDocumentLoaded(id), initialState, expectedState);
    }

    [TestMethod]
    [DataRow(default, default)]
    [DataRow(DocumentStateValue.Unloaded, DocumentStateValue.Missing)]
    [DataRow(DocumentStateValue.Loaded, default)]
    [DataRow(DocumentStateValue.Missing, default)]
    [DataRow(DocumentStateValue.LoadError, default)]
    [DataRow(DocumentStateValue.Opened, default)]
    public void OnDocumentMissing(DocumentStateValue? initialState, DocumentStateValue? expectedState)
    {
        var id = new TextDocumentIdentifier(TestDocumentUri1);
        TestDocumentStateTransition(sut => sut.OnDocumentMissing(id), initialState, expectedState);
    }

    [TestMethod]
    [DataRow(default, default)]
    [DataRow(DocumentStateValue.Unloaded, DocumentStateValue.LoadError)]
    [DataRow(DocumentStateValue.Loaded, default)]
    [DataRow(DocumentStateValue.Missing, default)]
    [DataRow(DocumentStateValue.LoadError, default)]
    [DataRow(DocumentStateValue.Opened, default)]
    public void OnDocumentLoadError(DocumentStateValue? initialState, DocumentStateValue? expectedState)
    {
        var id = new TextDocumentIdentifier(TestDocumentUri1);
        TestDocumentStateTransition(sut => sut.OnDocumentLoadError(id), initialState, expectedState);
    }

    [TestMethod]
    [DataRow(default, default)]
    [DataRow(DocumentStateValue.Unloaded, default)]
    [DataRow(DocumentStateValue.Loaded, DocumentStateValue.Opened)]
    [DataRow(DocumentStateValue.Missing, default)]
    [DataRow(DocumentStateValue.LoadError, default)]
    [DataRow(DocumentStateValue.Opened, default)]
    public void OnDocumentOpened(DocumentStateValue? initialState, DocumentStateValue? expectedState)
    {
        var id = new TextDocumentIdentifier(TestDocumentUri1);
        TestDocumentStateTransition(sut => sut.OnDocumentOpened(id), initialState, expectedState);
    }

    [TestMethod]
    [DataRow(default, default)]
    [DataRow(DocumentStateValue.Unloaded, default)]
    [DataRow(DocumentStateValue.Loaded, default)]
    [DataRow(DocumentStateValue.Missing, default)]
    [DataRow(DocumentStateValue.LoadError, default)]
    [DataRow(DocumentStateValue.Opened, DocumentStateValue.Loaded)]
    public void OnDocumentClosed(DocumentStateValue? initialState, DocumentStateValue? expectedState)
    {
        var id = new TextDocumentIdentifier(TestDocumentUri1);
        TestDocumentStateTransition(sut => sut.OnDocumentClosed(id), initialState, expectedState);
    }

    private static void TestDocumentStateTransition(Action<DocumentStateProvider> act, DocumentStateValue? initialState, DocumentStateValue? expectedState)
    {
        // arrange
        var id1 = new TextDocumentIdentifier(TestDocumentUri1);
        var id2 = new TextDocumentIdentifier(TestDocumentUri2);
        var sut = new DocumentStateProvider(_logger);

        if (initialState.HasValue)
        {
            sut.Initialize([id1, id2]);
            switch (initialState.Value)
            {
                case DocumentStateValue.Unloaded:
                    // already in that state, nothing to do.
                    break;
                case DocumentStateValue.Loaded:
                    sut.OnDocumentLoaded(id1);
                    break;
                case DocumentStateValue.Missing:
                    sut.OnDocumentMissing(id1);
                    break;
                case DocumentStateValue.LoadError:
                    sut.OnDocumentLoadError(id1);
                    break;
                case DocumentStateValue.Opened:
                    sut.OnDocumentLoaded(id1);
                    sut.OnDocumentOpened(id1);
                    break;
            }
        }

        if (expectedState.HasValue)
        {
            act.Invoke(sut);

            var result = sut.GetCurrentState(id1);
            Assert.AreEqual(expectedState, result.Value);
        }
        else
        {
            Assert.Throws<InvalidDocumentStateException>(() => act.Invoke(sut));
        }
    }
}
