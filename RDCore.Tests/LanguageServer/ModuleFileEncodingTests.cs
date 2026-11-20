using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI.App.Bench;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.Runtime.Support.Host.Symbols;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using System.IO.Abstractions.TestingHelpers;
using System.Text;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A module file is read in the encoding it is in (<see cref="ModuleFileText"/>): its byte order mark, otherwise UTF-8 when it is valid UTF-8, otherwise
/// the environment's ANSI code page, the one VBA exports in. The language server, the environment host and the console read it alike.
/// </summary>
[TestClass]
public sealed class ModuleFileEncodingTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-encoding-ws");

    private const string Source =
        "Attribute VB_Name = \"Module1\"\r\nSub Main()\r\n    Debug.Print \"Erreur 11 à la ligne\"\r\n    Debug.Print \"Deuxième\"\r\nEnd Sub\r\n";

    private readonly MockFileSystem _files = new();
    private readonly DocumentStateProvider _state = new(NullLogger<DocumentStateProvider>.Instance);

    private static Uri UriOf(string name) => new(Path.Combine(Root, name));

    private static Encoding CodePage(int codePage) => CodePagesEncodingProvider.Instance.GetEncoding(codePage)!;

    private WorkspaceDocumentService Sut(SdkAppOptions? options = null)
    {
        var service = new WorkspaceDocumentService(_state, NullLogger<WorkspaceDocumentService>.Instance, _files.Path, _files.File,
            options is null ? null : Microsoft.Extensions.Options.Options.Create(options));
        service.Initialize(Root);
        return service;
    }

    private async Task<string> LoadedText(byte[] bytes, SdkAppOptions? options = null)
    {
        _files.AddFile(Path.Combine(Root, "Module1.bas"), new MockFileData(bytes));
        var sut = Sut(options);
        var id = new TextDocumentIdentifier(UriOf("Module1.bas"));
        _state.Initialize([id]);

        Assert.IsTrue(await sut.TryLoadAsync(id));
        Assert.IsTrue(sut.TryGetDocument(UriOf("Module1.bas"), out var document));
        return document.Text;
    }

    // ---- the language server ----

    [TestMethod]
    public async Task AModuleFileInWindows1252_IsReadAsWindows1252()
    {
        var text = await LoadedText(CodePage(1252).GetBytes(Source));

        Assert.AreEqual(Source, text);
        StringAssert.Contains(text, "Erreur 11 à la ligne");
        StringAssert.Contains(text, "Deuxième");
    }

    [TestMethod]
    public async Task AModuleFileInUtf8_WithoutAByteOrderMark_IsReadAsUtf8()
    {
        var text = await LoadedText(new UTF8Encoding(false).GetBytes(Source));

        Assert.AreEqual(Source, text);
    }

    [TestMethod]
    public async Task AModuleFileInUtf8_WithAByteOrderMark_IsReadAsUtf8_WithoutTheMark()
    {
        var text = await LoadedText([.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(Source)]);

        Assert.AreEqual(Source, text);
    }

    [TestMethod]
    public async Task AModuleFile_IsReadInTheCodePageOfTheEnvironment()
    {
        const string cyrillic = "Attribute VB_Name = \"Module1\"\r\nSub Main()\r\n    Debug.Print \"Ошибка 11 в строке\"\r\nEnd Sub\r\n";
        var options = new SdkAppOptions { Environment = new SdkEnvironmentOptions { AnsiCodePage = 1251 } };

        var text = await LoadedText(CodePage(1251).GetBytes(cyrillic), options);

        Assert.AreEqual(cyrillic, text);
    }

    [TestMethod]
    public async Task AModuleFileInWindows1252_IsReadAsWindows1252_WhenTheClientClosesIt()
    {
        _files.AddFile(Path.Combine(Root, "Module1.bas"), new MockFileData(CodePage(1252).GetBytes(Source)));
        var sut = Sut();
        var id = new TextDocumentIdentifier(UriOf("Module1.bas"));
        _state.Initialize([id]);
        await sut.TryLoadAsync(id);
        sut.Open(UriOf("Module1.bas"), "Sub Main()\r\nEnd Sub\r\n", version: 1);

        var closed = await sut.CloseAsync(UriOf("Module1.bas"));

        Assert.IsNotNull(closed);
        Assert.AreEqual(Source, closed.Text);
    }

    // ---- the environment host ----

    [TestMethod]
    public void TheNameOfAModuleInWindows1252_IsReadAsWindows1252()
    {
        var source = "Attribute VB_Name = \"Données\"\r\nSub Main()\r\nEnd Sub\r\n";
        _files.AddFile(Path.Combine(Root, "Donnees.bas"), new MockFileData(CodePage(1252).GetBytes(source)));
        var project = new RDCoreProject { Name = "Project1", Modules = [new RDCoreModule { RelativeUri = "Donnees.bas" }] };

        var modules = new ProjectSymbolProvider(new Uri(Root), project, _files).ProvideSymbols().OfType<VBModuleSymbol>().ToArray();

        Assert.AreEqual("Données", modules.Single().Name);
    }

    // ---- the console: rdc run, rdc build ----

    [TestMethod]
    public async Task AModuleFileInWindows1252_IsReadAsWindows1252_WhenTheConsoleRunsIt()
    {
        var path = Path.Combine(Root, "Module1.bas");
        _files.AddFile(path, new MockFileData(CodePage(1252).GetBytes(Source)));

        var workspace = await BenchWorkspace.LoadAsync(_files, new ProjectFileLoader(_files), path);

        Assert.AreEqual(Source, workspace.Modules.Single().Source);
    }
}
