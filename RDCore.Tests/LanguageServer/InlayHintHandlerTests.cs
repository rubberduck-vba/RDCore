using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>textDocument/inlayHint</c> answers a parameter hint before each positional argument of the calls in the range, in the text of the document.
/// </summary>
[TestClass]
public sealed class InlayHintHandlerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-inlay-hint-ws");

    private const string Program = "10 MsgBox \"Hello\", vbOKOnly\r\n";

    private static InlayHintHandler Sut(string text = Program)
    {
        var documents = Substitute.For<IWorkspaceDocumentService>();
        var parsing = Substitute.For<IParsingClientService>();
        documents.WorkspaceRoot.Returns(Root);
        var program = new WorkspaceDocument("Main.rdc", Root, text, version: 1);
        documents.GetAllDocuments().Returns([program]);
        var uri = program.Id.Uri.ToUri();
        parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(program))));

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        return new InlayHintHandler(new RenameService(documents, parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance));
    }

    [TestMethod]
    public async Task EachPositionalArgument_IsHinted_InTheLinesOfAProgram()
    {
        var hints = (await Sut().Handle(new InlayHintParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(Path.Combine(Root, "Main.rdc"))),
            Range = new Range(0, 0, 1, 0),
        }, CancellationToken.None))!.ToArray();

        CollectionAssert.AreEqual(new[] { "Prompt:", "Buttons:" }, hints.Select(hint => hint.Label.String).ToArray());
        CollectionAssert.AreEqual(new[] { new Position(0, 10), new Position(0, 19) }, hints.Select(hint => hint.Position).ToArray());
        Assert.IsTrue(hints.All(hint => hint.Kind == InlayHintKind.Parameter && hint.PaddingRight == true));
    }

    [TestMethod]
    public async Task ADeclarationThatStatesNoType_IsHintedWithTheVariantItIs_AfterItsName()
    {
        var hints = (await Sut("10 Dim x\r\n20 Dim y As Long\r\n30 Dim z$\r\n").Handle(new InlayHintParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(Path.Combine(Root, "Main.rdc"))),
            Range = new Range(0, 0, 3, 0),
        }, CancellationToken.None))!.ToArray();

        var variant = hints.Single(hint => hint.Kind == InlayHintKind.Type);
        Assert.AreEqual("As Variant", variant.Label.String);
        Assert.AreEqual(new Position(0, 8), variant.Position, "x ends at the eighth character of `10 Dim x`");
        Assert.IsTrue(variant.PaddingLeft == true);
    }
}
