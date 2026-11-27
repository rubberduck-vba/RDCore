using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Completion;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Annotations;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Configuration;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The annotations completion offers after a <c>'@</c>: those of every extension of the platform that are legal where the annotation is written, each
/// described in the language of the user interface, and what an annotation takes, as signature help shows a procedure's arguments.
/// </summary>
[TestClass]
public sealed class AnnotationCompletionTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-annotation-completion-ws");

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly PlatformAnnotations _annotations = new();

    // an extension's annotation beside the core extension's: legal above a procedure of a standard module, described in both languages.
    private static readonly AnnotationSet Web = new()
    {
        Declarations =
        [
            new AnnotationDeclaration
            {
                Name = "Route",
                Targets = AnnotationTargets.Member,
                ModuleKinds = AnnotationModuleKinds.Standard,
                Parameters =
                [
                    new() { Name = "Path", Takes = AnnotationArgumentKind.Text },
                    new() { Name = "Verbs", Takes = AnnotationArgumentKind.Identifier, IsOptional = true, Repeats = true },
                ],
            },
        ],
        Descriptions = new()
        {
            ["en"] = new() { ["Route"] = "Routes a request.", ["Route_Path"] = "The path.", ["Route_Verbs"] = "The verbs." },
            ["fr"] = new() { ["Route"] = "Achemine une requête.", ["Route_Path"] = "Le chemin.", ["Route_Verbs"] = "Les verbes." },
        },
    };

    public AnnotationCompletionTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));
        _annotations.Add(ExtensionAnnotationsAttribute.Read(typeof(CoreAnnotations).Assembly)!);
        _annotations.Add(Web);
    }

    private CompletionService Sut()
    {
        var source = new InMemoryLibrarySource();
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        return new CompletionService(_documents, _parsing, new IntrinsicSymbolResolver(), Substitute.For<IReferencedLibraryService>(), source,
            new ClientVerbRegistry(), options, NullLogger<CompletionService>.Instance, OneProject.Registry(), annotations: _annotations);
    }

    // the module with the cursor where the `|` is, which is taken out of the source.
    private static (string Source, Position Position) At(string source)
    {
        var lines = source.ReplaceLineEndings("\r\n").Split("\r\n");
        var line = Array.FindIndex(lines, text => text.Contains('|'));
        var position = new Position(line, lines[line].IndexOf('|'));
        lines[line] = lines[line].Replace("|", string.Empty);
        return (string.Join("\r\n", lines), position);
    }

    private async Task<CompletionItem[]> ItemsAsync(string module, string? triggerCharacter = "@")
    {
        var (source, position) = At(module);
        return [.. (await Sut().CompleteFragmentAsync("Main", source, position, CancellationToken.None, triggerCharacter)).Items];
    }

    private async Task<string[]> LabelsAsync(string module, string? triggerCharacter = "@")
        => [.. (await ItemsAsync(module, triggerCharacter)).Select(item => item.Label)];

    private const string Standard = """
        Attribute VB_Name = "Main"
        {0}
        Option Explicit

        Public Sub Start()
        End Sub
        {1}
        Public Sub Run()
        End Sub
        """;

    private const string Class = """
        VERSION 1.0 CLASS
        BEGIN
          MultiUse = -1  'True
        END
        Attribute VB_Name = "Widget"
        '@|
        Option Explicit

        Public Sub Grow()
        End Sub
        """;

    [TestMethod]
    public async Task AtTheTopOfAStandardModule_TheModulesAnnotationsAreOffered_ForItsKind()
    {
        var labels = await LabelsAsync(string.Format(Standard, "'@|", string.Empty));

        CollectionAssert.IsSubsetOf(new[] { "ModuleDescription", "IgnoreModule", "Description" }, labels);
        CollectionAssert.DoesNotContain(labels, "PredeclaredId", "a class module's");
        CollectionAssert.DoesNotContain(labels, "Route", "a member's");
        CollectionAssert.DoesNotContain(labels, "Folder", "deprecated");
    }

    [TestMethod]
    public async Task AboveAMember_TheMembersAnnotationsAreOffered_TheExtensionsAmongThem()
    {
        var labels = await LabelsAsync(string.Format(Standard, string.Empty, "'@|"));

        CollectionAssert.IsSubsetOf(new[] { "Description", "EntryPoint", "Route" }, labels);
        CollectionAssert.DoesNotContain(labels, "ModuleDescription", "a module's");
        CollectionAssert.DoesNotContain(labels, "DefaultMember", "a class module's");
    }

    [TestMethod]
    public async Task AtTheTopOfAClassModule_AClassesAnnotationsAreOffered()
    {
        var labels = await LabelsAsync(Class);

        CollectionAssert.IsSubsetOf(new[] { "PredeclaredId", "Interface", "ModuleDescription" }, labels);
        CollectionAssert.DoesNotContain(labels, "Route", "a member's");
    }

    // the file's own module is a standard one, and the comment line directly above the block's header is the block's: the class's annotations are offered.
    [TestMethod]
    public async Task AboveABlocksHeader_TheBlocksModuleAnnotationsAreOffered_ForItsKind()
    {
        const string Script = "WScript.Echo \"go\"\r\n\r\n'@\r\nClass Account\r\nPublic Balance\r\nEnd Class\r\n";
        var backup = new WorkspaceDocument("Backup.vbs", Root, Script);
        var uri = backup.Id.Uri.ToUri();
        var parse = new ModuleParser().Parse(uri, Script);
        Assert.HasCount(1, parse.BlockModules, "the script's block");
        _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = backup; return true; });
        _parsing.TryGetCached(uri, out Arg.Any<ModuleParseResult>()).Returns(call => { call[1] = parse; return true; });

        var list = await Sut().CompleteDocumentAsync(uri, new Position(2, 2), CancellationToken.None, "@");
        var labels = list.Items.Select(item => item.Label).ToArray();

        CollectionAssert.IsSubsetOf(new[] { "PredeclaredId", "Interface" }, labels);
        CollectionAssert.DoesNotContain(labels, "ExcelHotkey", "a standard module's");
    }

    [TestMethod]
    [DataRow("    total@|", DisplayName = "the type hint of a Currency")]
    [DataRow("    s = \"'@|", DisplayName = "a string literal")]
    [DataRow("    ' see '@|", DisplayName = "the text of a comment")]
    public async Task AnAtThatStartsNoAnnotation_IsAnsweredNothing(string line)
    {
        var module = string.Format(Standard, string.Empty, string.Empty).Replace("Public Sub Start()", $"Public Sub Start()\r\n{line}");

        Assert.IsEmpty(await LabelsAsync(module));
    }

    [TestMethod]
    public async Task WhatIsTypedOfTheName_NarrowsTheList()
    {
        var labels = await LabelsAsync(string.Format(Standard, "'@Ign|", string.Empty), triggerCharacter: null);

        CollectionAssert.Contains(labels, "IgnoreModule");
        CollectionAssert.DoesNotContain(labels, "ModuleDescription");
    }

    [TestMethod]
    [DataRow("en-CA", "Routes a request.")]
    [DataRow("fr-CA", "Achemine une requête.")]
    public async Task AnOfferedAnnotation_IsDescribedInTheLanguageOfTheUserInterface(string culture, string description)
    {
        var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var route = (await ItemsAsync(string.Format(Standard, string.Empty, "'@|"))).Single(item => item.Label == "Route");

            Assert.AreEqual(description, route.Documentation?.String);
            Assert.AreEqual("@Route(Path, ParamArray Verbs())", route.Detail);
        }
        finally
        {
            CultureInfo.CurrentUICulture = ui;
        }
    }

    [TestMethod]
    public async Task ACoreAnnotation_IsDescribedInFrench_ByTheCoreExtensionsResources()
    {
        var ui = CultureInfo.CurrentUICulture;
        var core = new AnnotationCatalogue([ExtensionAnnotationsAttribute.Read(typeof(CoreAnnotations).Assembly)!]);
        var moduleDescription = core.Find("ModuleDescription")!;
        var french = core.DescriptionOf(moduleDescription, CultureInfo.GetCultureInfo("fr"));
        Assert.AreNotEqual(core.DescriptionOf(moduleDescription, CultureInfo.GetCultureInfo("en")), french, "the resources have a French text of their own");
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-CA");
            var item = (await ItemsAsync(string.Format(Standard, "'@|", string.Empty))).Single(each => each.Label == "ModuleDescription");

            Assert.AreEqual(french, item.Documentation?.String);
        }
        finally
        {
            CultureInfo.CurrentUICulture = ui;
        }
    }

    private async Task<SignatureHelp?> SignatureAsync(string line)
    {
        var (source, position) = At(string.Format(Standard, string.Empty, line));
        return await Sut().SignatureHelpFragmentAsync("Main", source, position, CancellationToken.None);
    }

    private static string ActiveParameterOf(SignatureHelp help)
    {
        var signature = help.Signatures.Single();
        var (start, end) = signature.Parameters!.ElementAt(help.ActiveParameter!.Value).Label.Range;
        return signature.Label[start..end];
    }

    [TestMethod]
    [DataRow("'@Route(|", "Path")]
    [DataRow("'@Route(\"/orders\", |", "ParamArray Verbs()")]
    [DataRow("'@Route \"/orders\", Get, |", "ParamArray Verbs()")]
    public async Task AnAnnotationsArguments_AreGivenItsSignature_AsAProceduresAre(string line, string active)
    {
        var help = await SignatureAsync(line);

        Assert.IsNotNull(help);
        Assert.AreEqual("@Route(Path, ParamArray Verbs())", help.Signatures.Single().Label);
        Assert.AreEqual(active, ActiveParameterOf(help));
    }

    [TestMethod]
    public async Task TheArgumentsOfAnAnnotationNoExtensionDeclares_HaveNoSignature()
        => Assert.IsNull(await SignatureAsync("'@Unheard(|"));
}
