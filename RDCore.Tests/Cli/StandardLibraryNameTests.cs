using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The name of the standard library is the language's: <c>VBA</c> in RD-VBA, <c>VB</c> in VB6 and <c>RDC</c> in the platform's BASIC. It is what a
/// project-qualified reference to the library names (<strong>MS-VBAL §5.6.12</strong>), and every component that builds the library's
/// symbols - the language server over the workspace, the environment host over its session - has to agree on it.
/// </summary>
[TestClass]
public sealed class StandardLibraryNameTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-language-ws");

    [TestMethod]
    [DataRow("vba", "VBA")]
    [DataRow("vb6", "VB")]
    [DataRow("basic", "RDC")]
    [DataRow("BASIC", "RDC", DisplayName = "an identifier is compared without regard to case")]
    public void EachLanguage_HasItsOwnStandardLibraryName(string id, string expected)
        => Assert.AreEqual(expected, SupportedLanguages.Get(id).StandardLibraryName);

    [TestMethod]
    public void TheDefaultLanguage_IsRDVBA_WhoseLibraryIsVBA()
    {
        var options = new SdkWorkspaceOptions();

        Assert.AreSame(SupportedLanguages.RDVBA, options.SupportedLanguage);
        Assert.AreEqual("VBA", options.SupportedLanguage.StandardLibraryName);
    }

    [TestMethod]
    public void AWorkspacesLanguage_IsTheLanguageItsOptionNames()
        => Assert.AreSame(SupportedLanguages.BASIC, new SdkWorkspaceOptions { Language = "basic" }.SupportedLanguage);

    [TestMethod]
    public void ALanguageThePlatformDoesNotServe_IsRefusedAndTheMessageSaysWhichItDoes()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => _ = new SdkWorkspaceOptions { Language = "cobol" }.SupportedLanguage);

        StringAssert.Contains(failure.Message, "cobol");
        StringAssert.Contains(failure.Message, "vba, vb6, basic");
    }

    [TestMethod]
    public void EveryLanguage_IsFoundByItsOwnIdentifier()
    {
        foreach (var language in SupportedLanguages.All)
        {
            Assert.IsTrue(SupportedLanguages.TryGet(language.Id, out var found));
            Assert.AreSame(language, found);
        }
    }

    // the whole path of a qualified call, as the platform makes it: the language server's composition over the workspace, the host's session, the run.
    private static async Task<ExecuteSessionResult> RunAsync(string standardLibraryName, string expression)
    {
        const string ModuleName = "Program";
        var source = $"Attribute VB_Name = \"{ModuleName}\"\r\nPublic Sub Main()\r\nDebug.Print {expression}\r\nEnd Sub\r\n";
        var project = new ProjectFile(Root, new RDCoreProject { Name = ModuleName, Modules = [new RDCoreModule { RelativeUri = $"{ModuleName}.bas" }] });
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)),
            [Path.Combine(Root, $"{ModuleName}.bas")] = new(source),
        });

        var sessionProvider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), fs, NullLogger<EnvironmentSessionProvider>.Instance, standardLibraryName);
        var workspaceRoot = new Uri(Root);
        sessionProvider.Compose(project.ProjectInfo, workspaceRoot);

        var moduleUri = new UriBuilder(workspaceRoot) { Fragment = ModuleName }.Uri;
        var parse = new ModuleParser().Parse(new Uri(Path.Combine(Root, $"{ModuleName}.bas")), source);
        Assert.IsTrue(parse.IsSuccess);

        var resolver = WorkspaceSymbolResolver.Compose(
            workspaceRoot, [(moduleUri, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver(), standardLibraryName: standardLibraryName);
        var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, ModuleType.StdModule, parse, resolver, withImplicitDeclarations: true).ProvideSymbols();

        await new DefineSymbolsHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance)
            .Handle(new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = ModuleName,
                Symbols = SymbolDescriptorProjector.Project(symbols, moduleUri),
                Directives = parse.SyntaxTree.GetModuleDirectives(),
                Replace = true,
            }, CancellationToken.None);

        return await new HostExecuteHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<HostExecuteHandler>.Instance)
            .Handle(new HostExecuteParams
            {
                Json = PlatformJson.Serialize(new HostExecutePayload(moduleUri, parse)),
                ModuleName = ModuleName,
                EntryPoint = "Main",
            }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow("VBA")]
    [DataRow("VB")]
    [DataRow("RDC")]
    public async Task AQualifiedCall_NamesTheLibraryOfTheLanguage(string library)
    {
        var result = await RunAsync(library, $"{library}.LenB(\"42\")");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "4" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task TheNameOfAnotherLanguagesLibrary_IsNotTheLibrary()
    {
        // in the platform's BASIC the library is RDC: `VBA` names nothing.
        var result = await RunAsync("RDC", "VBA.LenB(\"42\")");

        Assert.AreNotEqual(ExecutionOutcome.Completed, result.Outcome);
    }

    [TestMethod]
    public async Task TheLibrarysMembers_StillResolveUnqualified_WhateverItIsCalled()
    {
        var result = await RunAsync("RDC", "LenB(\"42\")");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "4" }, result.Output.Select(line => line.Trim()).ToArray());
    }
}
