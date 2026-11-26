using NSubstitute;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The branches the environment host said are dead of a module, as the language server keeps them: read again in a parse of an edit of the module whose blocks
/// have the same shape, and not known of a parse of other blocks, of a document that changed since, or of a parse the host was not sent of the document as it
/// reads.
/// </summary>
[TestClass]
public sealed class ModuleLiveBranchesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-livebranches-ws");

    private const string Text =
        "Option Explicit\r\n" +
        "#If Win64 Then\r\n" +
        "Public Sub A()\r\n" +
        "End Sub\r\n" +
        "#Else\r\n" +
        "Public Sub B()\r\n" +
        "End Sub\r\n" +
        "#End If\r\n";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly WorkspaceDocument _document = new("Module1.bas", Root, Text, version: 2);
    private readonly Uri _uri;
    private readonly ModuleParseResult _parse;
    private readonly ModuleLiveBranches _branches;

    public ModuleLiveBranchesTests()
    {
        _uri = _document.Id.Uri.ToUri();
        _parse = new ModuleParser().Parse(_uri, Text);
        Current(_document);
        _parsing.TryGetCached(_uri, _document.Version, out Arg.Any<ModuleParseResult>()).Returns(call => { call[2] = _parse; return true; });
        _branches = new ModuleLiveBranches(_documents, _parsing);
    }

    // the document as the server holds it now.
    private void Current(WorkspaceDocument document)
        => _documents.TryGetDocument(_uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });

    // the body of the #If branch of the module's block, as the host names a dead branch.
    private static SourceLocation IfBranchOf(ModuleParseResult parse)
        => parse.PrecompilerTrivia.SelectMany(node => node.Descendants().Prepend(node)).OfType<PrecompilerIfBlockStatementNode>().First()
            .Children.OfType<PrecompilerTriviaNode>().First().SourceLocation;

    private static SourceLocation DeclarationOf(ModuleParseResult parse, string name)
        => parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single(member => member.Name == name).SourceLocation;

    [TestMethod]
    public void TheDeadBranch_IsDead_InTheParseTheHostWasSent_AndTheOtherIsNot()
    {
        _branches.Record(_parse, [IfBranchOf(_parse)]);

        var isDead = _branches.DeadTestFor(_parse);

        Assert.IsNotNull(isDead);
        Assert.IsTrue(isDead(DeclarationOf(_parse, "A")));
        Assert.IsFalse(isDead(DeclarationOf(_parse, "B")));
    }

    [TestMethod]
    public void TheDeadBranch_IsDead_InAnEditOfTheModuleWhoseBlocksHaveTheSameShape()
    {
        _branches.Record(_parse, [IfBranchOf(_parse)]);
        var edited = new ModuleParser().Parse(_uri, Text.Replace("Option Explicit\r\n", "Option Explicit\r\nPrivate Sub C()\r\nEnd Sub\r\n"));
        Assert.AreNotEqual(DeclarationOf(_parse, "A").Range, DeclarationOf(edited, "A").Range, "the edit moves the block");

        var isDead = _branches.DeadTestFor(edited);

        Assert.IsNotNull(isDead);
        Assert.IsTrue(isDead(DeclarationOf(edited, "A")));
        Assert.IsFalse(isDead(DeclarationOf(edited, "B")));
        Assert.IsFalse(isDead(DeclarationOf(edited, "C")));
    }

    [TestMethod]
    [DataRow("Option Explicit\r\n", "Option Explicit\r\n#If VBA7 Then\r\nPrivate Sub C()\r\nEnd Sub\r\n#End If\r\n", DisplayName = "a block added")]
    [DataRow("#Else\r\n", "#ElseIf Mac Then\r\nPublic Sub C()\r\nEnd Sub\r\n#Else\r\n", DisplayName = "a branch added to the block")]
    public void NothingIsKnown_OfAnEditWhoseBlocksHaveAnotherShape(string written, string edit)
    {
        _branches.Record(_parse, [IfBranchOf(_parse)]);
        var edited = new ModuleParser().Parse(_uri, Text.Replace(written, edit));

        Assert.IsNull(_branches.DeadTestFor(edited));
        Assert.IsNotNull(_branches.DeadTestFor(_parse), "what was recorded is known of the parse the host was sent");
    }

    [TestMethod]
    public void NothingIsKnown_OnceTheDocumentHasChanged()
    {
        _branches.Record(_parse, [IfBranchOf(_parse)]);
        Assert.IsNotNull(_branches.DeadTestFor(_parse), "what was recorded is known before the document changes");

        Current(_document.WithText(Text + "\r\n"));

        Assert.IsNull(_branches.DeadTestFor(_parse));
    }

    [TestMethod]
    public void WhatTheHostSaysOfAParseOfAnotherText_IsNotRecorded_AndWhatWasRecordedIsForgotten()
    {
        _branches.Record(_parse, [IfBranchOf(_parse)]);
        var older = new ModuleParser().Parse(_uri, Text);

        _branches.Record(older, [IfBranchOf(older)]);

        Assert.IsNull(_branches.DeadTestFor(_parse));
    }
}
