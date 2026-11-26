using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Navigation;
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

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What a hover says a module, a member or a variable is: the description its module writes (an attribute, else an annotation), or the help string of its
/// library, after its declaration.
/// </summary>
[TestClass]
public sealed class DescriptionHoverTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-description-hover-ws");

    // a module that writes each kind of description: an attribute and an annotation for the module and a function, each saying another thing; an
    // annotation for a procedure and a variable, an attribute for another variable.
    internal const string Counter = """
        Attribute VB_Name = "Counter"
        Attribute VB_Description = "Counts things."
        '@ModuleDescription("Not what the module says.")
        '@VariableDescription("How many there are.")
        Public Total As Long
        Public Limit As Long
        Attribute Limit.VB_VarDescription = "The most there can be."

        '@Description("Adds to the count.")
        Public Sub Increment(ByVal By As Long)
        End Sub

        '@Description("Not what the function says.")
        Public Function Reset() As Long
        Attribute Reset.VB_Description = "Sets the count back to zero."
        End Function
        """;

    // a module that writes its description with an annotation alone, as a file of the platform writes it.
    internal const string Tally = """
        Attribute VB_Name = "Tally"
        '@ModuleDescription("Keeps a tally.")
        Public Sub Mark()
        End Sub
        """;

    private const string Main = """
        Attribute VB_Name = "Main"
        Public Sub Run()
            Counter.Increment 1
            x = Counter.Reset
            Counter.Total = Counter.Limit
            Tally.Mark
            x = labRound
        End Sub
        """;

    // a library whose enumeration member has a help string.
    private static readonly LibraryDescription Lab = new()
    {
        Name = "Lab",
        Enums =
        [
            new EnumDescription
            {
                Name = "Shape",
                Members = [new EnumMemberDescription { Name = "labRound", Value = 1, Description = "A round jar." }],
            },
        ],
    };

    private static Uri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName)).ToUri();

    private static RenameService Workspace()
    {
        var documents = Substitute.For<IWorkspaceDocumentService>();
        var parsing = Substitute.For<IParsingClientService>();
        documents.WorkspaceRoot.Returns(Root);
        WorkspaceDocument[] modules =
        [
            new("Counter.bas", Root, Counter, version: 1), new("Tally.bas", Root, Tally, version: 1), new("Main.bas", Root, Main, version: 1),
        ];
        documents.GetAllDocuments().Returns(modules);
        foreach (var module in modules)
        {
            var uri = module.Id.Uri.ToUri();
            parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(module))));
        }

        var source = new InMemoryLibrarySource(Lab);
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(source, [new LibraryIdentity("Lab")], [StandardLibrary.LibraryName]))
                .ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        return new RenameService(
            documents, parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
    }

    // the Markdown hover over a name of Main, inside the first place the text that holds it is written.
    private static async Task<string> HoverAsync(string written, string name)
    {
        var lines = Main.ReplaceLineEndings("\n").Split('\n');
        var line = Array.FindIndex(lines, text => text.Contains(written));
        var position = new Position(line, lines[line].IndexOf(written) + written.IndexOf(name) + 1);
        var request = new HoverParams { TextDocument = new TextDocumentIdentifier(DocumentUri.From(Document("Main.bas"))), Position = position };

        var hover = await new HoverHandler(Workspace()).Handle(request, CancellationToken.None);

        Assert.IsNotNull(hover, $"a hover over {name}");
        return hover.Contents.MarkupContent!.Value;
    }

    [TestMethod]
    public async Task AModulesHover_ShowsItsDescriptionAttribute_OverADifferentModuleDescriptionAnnotation()
    {
        var hover = await HoverAsync("Counter.Increment 1", "Counter");

        StringAssert.EndsWith(hover, "\n\nCounts things.");
        Assert.IsFalse(hover.Contains("Not what the module says."), hover);
    }

    [TestMethod]
    public async Task AModuleThatWritesNoDescriptionAttribute_ShowsItsModuleDescriptionAnnotation()
        => StringAssert.EndsWith(await HoverAsync("Tally.Mark", "Tally"), "\n\nKeeps a tally.");

    [TestMethod]
    public async Task AProceduresHover_ShowsTheDescriptionAnnotationAboveIt()
        => StringAssert.EndsWith(await HoverAsync("Counter.Increment 1", "Increment"), "\n\nAdds to the count.");

    [TestMethod]
    public async Task AFunctionsHover_ShowsItsDescriptionAttribute_OverADifferentAnnotation()
    {
        var hover = await HoverAsync("x = Counter.Reset", "Reset");

        StringAssert.EndsWith(hover, "\n\nSets the count back to zero.");
        Assert.IsFalse(hover.Contains("Not what the function says."), hover);
    }

    [TestMethod]
    public async Task AModuleVariablesHover_ShowsItsVariableDescriptionAnnotation_ElseItsVarDescriptionAttribute()
    {
        StringAssert.EndsWith(await HoverAsync("Counter.Total =", "Total"), "\n\nHow many there are.");
        StringAssert.EndsWith(await HoverAsync("= Counter.Limit", "Limit"), "\n\nThe most there can be.");
    }

    [TestMethod]
    public async Task ALibraryEnumerationMembersHover_ShowsItsHelpString()
        => StringAssert.EndsWith(await HoverAsync("x = labRound", "labRound"), "\n\nA round jar.");
}
