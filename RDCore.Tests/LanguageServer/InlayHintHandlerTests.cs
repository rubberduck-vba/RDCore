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
/// <c>textDocument/inlayHint</c> answers the hints of the range, in brackets at their places in the text of the document, of the kinds the settings show.
/// </summary>
[TestClass]
public sealed class InlayHintHandlerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-inlay-hint-ws");

    private const string Program = "10 MsgBox \"Hello\", vbOKOnly\r\n";

    private static InlayHintHandler Sut(string text = Program, SdkInlayHintOptions? sites = null)
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
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions
        {
            Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" },
            InlayHints = sites ?? new SdkInlayHintOptions(),
        });
        return new InlayHintHandler(new RenameService(documents, parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry()),
            options);
    }

    private static async Task<InlayHint[]> HintsAsync(InlayHintHandler handler, int lines)
        => (await handler.Handle(new InlayHintParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(Path.Combine(Root, "Main.rdc"))),
            Range = new Range(0, 0, lines, 0),
        }, CancellationToken.None))!.ToArray();

    [TestMethod]
    public async Task EachPositionalArgument_IsHinted_InBrackets_BeforeIt_InTheLinesOfAProgram()
    {
        var hints = await HintsAsync(Sut(), 1);

        CollectionAssert.AreEqual(new[] { "[Prompt:]", "[Buttons:]" }, hints.Select(hint => hint.Label.String).ToArray());
        CollectionAssert.AreEqual(new[] { new Position(0, 10), new Position(0, 19) }, hints.Select(hint => hint.Position).ToArray());
        Assert.IsTrue(hints.All(hint => hint.Kind == InlayHintKind.Parameter && hint.PaddingLeft != true && hint.PaddingRight != true), "[Prompt:]\"Hello\" reads as one");
    }

    [TestMethod]
    public async Task ADeclarationThatStatesNoType_IsHintedWithTheVariantItIs_AfterItsName()
    {
        var hints = await HintsAsync(Sut("10 Dim x\r\n20 Dim y As Long\r\n30 Dim z$\r\n"), 3);

        var variant = hints.Single(hint => hint.Kind == InlayHintKind.Type);
        Assert.AreEqual("[As Variant]", variant.Label.String);
        Assert.AreEqual(new Position(0, 8), variant.Position, "x ends at the eighth character of `10 Dim x`");
        Assert.IsTrue(variant.PaddingLeft == true);
    }

    // the module of a program is its lines in a procedure the program does not write: what is hinted of that procedure's header and end is not in the text.
    [TestMethod]
    public async Task ANextOfAProgram_IsHintedWithItsVariable_AndTheEndOfTheProcedureItIsInIsNot()
    {
        var hints = await HintsAsync(Sut("10 For i = 1 To 2\r\n20 Next\r\n"), 2);

        Assert.AreEqual(("[i]", new Position(1, 7)), (Assert.ContainsSingle(hints, string.Join(", ", hints.Select(hint => hint.Label.String))).Label.String, hints[0].Position));
    }

    [TestMethod]
    public async Task AKindOfHint_TurnedOffInTheSettings_IsNotAnswered()
        => Assert.IsEmpty(await HintsAsync(Sut("10 For i = 1 To 2\r\n20 Next\r\n", new SdkInlayHintOptions { NextVariables = false }), 2));
}
