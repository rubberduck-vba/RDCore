using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.SemanticTokens;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.Parsing;
using RDCore.SDK.Model.Source;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The semantic tokens of a document (<strong>LSP 3.17</strong> §Semantic Tokens): the lexical tokens the parsing server finds, and the names in them told apart by
/// what the module declares.
/// </summary>
[TestClass]
public sealed class SemanticTokensServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-semantic-tokens-ws");
    private static readonly Uri Mod1 = new(Path.Combine(Root, "Mod1.bas"));

    private const string Source = """
        Public Const Max = 10
        Public Enum Color
            Red
        End Enum
        Public Sub Foo()
            Dim x As Color
            x = Max
            y.Max = 1 ' done
        End Sub
        """;

    private readonly MockFileSystem _files = new();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly WorkspaceDocumentService _documents;
    private readonly SemanticTokensService _sut;

    public SemanticTokensServiceTests()
    {
        _documents = new WorkspaceDocumentService(
            new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance), NullLogger<WorkspaceDocumentService>.Instance, _files.Path, _files.File);
        _documents.Initialize(Root);

        _parsing.TokenizeAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new SyntaxTokenizer().Tokenize((string)call[1])));
        _parsing.ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse((Uri)call[0], _documents.TryGetDocument((Uri)call[0], out var document) ? document.Text : string.Empty)));

        _sut = new SemanticTokensService(_documents, _parsing);
    }

    private static int TypeIndex(string name) => SemanticTokenLegend.TokenTypes.ToList().IndexOf(name);

    private async Task<SemanticToken> TokenAtAsync(int line, int character)
    {
        var tokens = await _sut.GetAsync(Mod1, CancellationToken.None);
        return tokens.Single(token => token.Line == line && token.Character == character);
    }

    [TestMethod]
    public async Task ADocumentTheServerDoesNotHold_HasNoTokens()
        => Assert.IsEmpty(await _sut.GetAsync(Mod1, CancellationToken.None));

    [TestMethod]
    public async Task AKeyword_AStringAndAComment_AreTheTokenTypesOfTheLegend()
    {
        _documents.Open(Mod1, Source, 1);

        Assert.AreEqual(TypeIndex("keyword"), (await TokenAtAsync(5, 4)).Type);
        Assert.AreEqual(TypeIndex("comment"), (await TokenAtAsync(7, 14)).Type);
        Assert.AreEqual(TypeIndex("number"), (await TokenAtAsync(7, 12)).Type);
    }

    [TestMethod]
    public async Task ANameThatIsAConstantOfTheModule_IsReadOnly()
    {
        _documents.Open(Mod1, Source, 1);

        var max = await TokenAtAsync(6, 8);

        Assert.AreEqual(TypeIndex("variable"), max.Type);
        Assert.AreEqual(SemanticTokenLegend.ReadOnly, max.Modifiers);
    }

    [TestMethod]
    public async Task ANameThatIsATypeOfTheModule_IsAClass_WhereItIsUsed()
    {
        _documents.Open(Mod1, Source, 1);

        Assert.AreEqual(TypeIndex("class"), (await TokenAtAsync(5, 13)).Type);
    }

    [TestMethod]
    public async Task ANameThatIsAMemberOfSomething_IsNeverAConstant()
    {
        _documents.Open(Mod1, Source, 1);

        var member = await TokenAtAsync(7, 6);

        Assert.AreEqual(0, member.Modifiers);
    }

    [TestMethod]
    public async Task AClassModuleOfTheWorkspace_IsAType()
    {
        _documents.Open(new Uri(Path.Combine(Root, "Widget.cls")), "Public Sub Foo()\r\nEnd Sub", 1);
        _documents.Open(Mod1, "Dim w As Widget", 1);

        Assert.AreEqual(TypeIndex("class"), (await TokenAtAsync(0, 9)).Type);
    }

    [TestMethod]
    public void TheLegend_HasATypeForEveryKindOfToken()
    {
        foreach (var kind in Enum.GetValues<SyntaxTokenKind>())
        {
            Assert.IsLessThan(SemanticTokenLegend.TokenTypes.Count, SemanticTokenLegend.TypeOf(kind));
        }
    }
}

