using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using MemberKind = RDCore.SDK.Model.AST.Declarations.MemberKind;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Extract Method of a graphics statement written with no object, <c>Line (0, 0)-(x, x)</c>: in the code of a Visual Basic 6.0 form it draws on the form,
/// <c>Me</c>, and is extracted as any statement is; in any other module it is not defined.
/// </summary>
[TestClass]
public sealed class ExtractMethodFormCodeTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-extractmethod-form-ws");

    // Run draws with x, which the line before gives a value.
    private const string Code =
        "Option Explicit\r\n" +
        "Public Sub Run(ByVal n As Long)\r\n" +
        "    Dim x As Long\r\n" +
        "    x = n\r\n" +
        "    Line (0, 0)-(x, x)\r\n" +
        "End Sub\r\n";

    private const string Drawing = "    Line (0, 0)-(x, x)";

    private const string Form =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   ClientHeight    =   3000\r\n" +
        "   ClientWidth     =   4000\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n" +
        "Attribute VB_GlobalNameSpace = False\r\n" +
        "Attribute VB_Creatable = False\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Attribute VB_Exposed = False\r\n" + Code;

    private const string Class =
        "VERSION 1.0 CLASS\r\n" +
        "BEGIN\r\n" +
        "  MultiUse = -1  'True\r\n" +
        "END\r\n" +
        "Attribute VB_Name = \"Class1\"\r\n" + Code;

    private const string Standard = "Attribute VB_Name = \"Module1\"\r\n" + Code;

    private static readonly Dictionary<string, string> Texts = new() { ["Form1.frm"] = Form, ["Class1.cls"] = Class, ["Module1.bas"] = Standard };

    private readonly RenameService _rename;
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly IOptions<SdkAppOptions> _options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vb6" } });

    public ExtractMethodFormCodeTests()
    {
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.WorkspaceRoot.Returns(Root);
        var all = Texts.Select(text => new WorkspaceDocument(text.Key, Root, text.Value, version: 1)).ToList();
        documents.GetAllDocuments().Returns(all);
        foreach (var document in all)
        {
            var uri = document.Id.Uri.ToUri();
            documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            // the designer of a form is read from its document, as the parsing client reads it.
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ModuleParser().Parse(uri, document.Text) with { Designer = FormDesigner.Read(document.Text, default) }));
        }

        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        _rename = new RenameService(documents, _parsing, new IntrinsicSymbolResolver(), libraries, _options, NullLogger<RenameService>.Instance, OneProject.Registry());
    }

    private static Uri DocumentOf(string file) => DocumentUri.From(Path.Combine(Root, file)).ToUri();

    // the whole line of the text that is the statement drawn with.
    private static SourceRange DrawingLine(string text)
    {
        var line = Array.IndexOf(text.Split("\r\n"), Drawing);
        return new(line, 0, line + 1, 0);
    }

    [TestMethod]
    public async Task ABareLine_InTheCodeOfAVisualBasic6Form_IsExtracted_WithWhatItDrawsWith()
    {
        var answer = await new ExtractMethodService(_rename, _parsing, _options).ApplyAsync(
            DocumentOf("Form1.frm"), DrawingLine(Form), [new RefactorAnswer { Id = ExtractMethodService.NameChoice, Value = "Square" }], [], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var text = Applied(Form, answer.Edit!.Documents.Single().Edits);
        StringAssert.Contains(text, "Private Sub Square(ByVal x As Long)\r\n" + Drawing + "\r\n");
        StringAssert.Contains(text, "\r\n    x = n\r\n    Square x\r\n");
    }

    [TestMethod]
    [DataRow("Form1.frm", false)]
    [DataRow("Class1.cls", true)]
    [DataRow("Module1.bas", true)]
    public async Task ABareLine_IsNotDefined_OutsideTheCodeOfAVisualBasic6Form(string file, bool undefined)
    {
        var (part, module) = (await _rename.ComposeAsync(CancellationToken.None))!.DocumentOf(DocumentOf(file))!.Value;
        var analysis = ExtractionModuleAnalysis.Of(module.Module, module.Source, module.Parse, part.Context.Resolver, part.Context.ScopeTree);

        var errors = analysis.Procedure("Run", MemberKind.Procedure)!.Model.CompileErrors;

        CollectionAssert.AreEqual(undefined ? new[] { VBCompileErrorId.SubOrFunctionNotDefined } : [], errors.Select(error => error.VBCompileErrorId).ToArray(),
            string.Join(" | ", errors.Select(error => error.Verbose)));
    }

    // the text with the edits made, each of the text as it was before any of them.
    private static string Applied(string text, IReadOnlyList<RefactorTextEdit> edits)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        int Offset(SourcePosition position) => starts[position.Line] + position.Character;
        foreach (var edit in edits.OrderByDescending(edit => Offset(edit.Range.Start)))
        {
            text = string.Concat(text.AsSpan(0, Offset(edit.Range.Start)), edit.NewText, text.AsSpan(Offset(edit.Range.End)));
        }

        return text;
    }
}
