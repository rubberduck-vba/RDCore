using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.Server;

/// <summary>
/// A WorkspaceEdit as a client can apply it (LSP 3.17 §WorkspaceEdit): documentChanges to a client that said it takes them, and the same edits as changes to
/// any other - which reads changes alone.
/// </summary>
[TestClass]
public sealed class WorkspaceEditExtensionsTests
{
    private static readonly DocumentUri First = DocumentUri.From("file:///c:/ws/Module1.bas");
    private static readonly DocumentUri Second = DocumentUri.From("file:///c:/ws/Module2.bas");

    private static TextEdit Edit(int line, string text) => new() { Range = new Range(line, 0, line, 3), NewText = text };

    private static WorkspaceEditDocumentChange Change(DocumentUri uri, int version, params TextEdit[] edits)
        => new(new TextDocumentEdit { TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = uri, Version = version }, Edits = new TextEditContainer(edits) });

    private static WorkspaceEdit Versioned() => new()
    {
        DocumentChanges = new Container<WorkspaceEditDocumentChange>(Change(First, 4, Edit(0, "a"), Edit(2, "b")), Change(Second, 7, Edit(1, "c"))),
    };

    private static ClientCapabilities Taking(bool documentChanges)
        => new() { Workspace = new WorkspaceClientCapabilities { WorkspaceEdit = new WorkspaceEditCapability { DocumentChanges = documentChanges } } };

    [TestMethod]
    public void AClientThatTakesDocumentChanges_IsSentTheEditAsItIs()
    {
        var edit = Versioned();

        Assert.AreSame(edit, edit.For(Taking(documentChanges: true)));
    }

    [TestMethod]
    [DataRow(false, DisplayName = "a client that says it does not")]
    [DataRow(null, DisplayName = "a client whose capabilities are not known")]
    public void AnyOtherClient_IsSentTheSameEditsAsChanges_ByDocument_InOrder(bool? documentChanges)
    {
        var sent = Versioned().For(documentChanges is { } takes ? Taking(takes) : null);

        Assert.IsNull(sent.DocumentChanges);
        CollectionAssert.AreEqual(new[] { "a", "b" }, sent.Changes![First].Select(edit => edit.NewText).ToArray());
        CollectionAssert.AreEqual(new[] { "c" }, sent.Changes[Second].Select(edit => edit.NewText).ToArray());
    }

    [TestMethod]
    public void WhatWasInChangesAlready_IsKept()
    {
        var edit = Versioned() with { Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>> { [First] = [Edit(5, "z")] } };

        var sent = edit.For(Taking(documentChanges: false));

        CollectionAssert.AreEqual(new[] { "z", "a", "b" }, sent.Changes![First].Select(change => change.NewText).ToArray());
    }

    // changes can't say that a file is created, renamed or deleted: no client that does not take documentChanges can apply such an edit, which is sent as it is.
    [TestMethod]
    public void AnEditThatCreatesAFile_IsSentAsItIs()
    {
        var edit = new WorkspaceEdit
        {
            DocumentChanges = new Container<WorkspaceEditDocumentChange>(new WorkspaceEditDocumentChange(new CreateFile { Uri = Second }), Change(First, 4, Edit(0, "a"))),
        };

        Assert.AreSame(edit, edit.For(Taking(documentChanges: false)));
    }
}
