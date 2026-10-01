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
/// The languages the platform serves: how one is found by its identifier, which one a workspace is written in, and - through
/// <see cref="RunAsync"/> - the whole path a program takes in the environment host, as the platform makes it.
/// </summary>
[TestClass]
public sealed class SupportedLanguageTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-language-ws");

    [TestMethod]
    [DataRow("vba")]
    [DataRow("vb6")]
    [DataRow("basic")]
    [DataRow("BASIC", DisplayName = "an identifier is compared without regard to case")]
    public void EachLanguage_IsFoundByItsIdentifier(string id)
        => Assert.AreEqual(id, SupportedLanguages.Get(id).Id, ignoreCase: true);

    [TestMethod]
    public void TheDefaultLanguage_IsRDVBA()
        => Assert.AreSame(SupportedLanguages.RDVBA, new SdkWorkspaceOptions().SupportedLanguage);

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

    // the whole path of a program, as the platform makes it: the language server's composition over the workspace, the host's session, the run.
    // `statement` is the body of Main when it is not just a Debug.Print of the expression; `language` is the one the environment is a dialect of.
    internal static async Task<ExecuteSessionResult> RunAsync(string expression, string? statement = null, SupportedLanguage? language = null)
    {
        const string ModuleName = "Program";
        var source = $"Attribute VB_Name = \"{ModuleName}\"\r\nPublic Sub Main()\r\n{statement ?? $"Debug.Print {expression}"}\r\nEnd Sub\r\n";
        var project = new ProjectFile(Root, new RDCoreProject { Name = ModuleName, Modules = [new RDCoreModule { RelativeUri = $"{ModuleName}.bas" }] });
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)),
            [Path.Combine(Root, $"{ModuleName}.bas")] = new(source),
        });

        var sessionProvider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, SourceLanguage: language), fs, NullLogger<EnvironmentSessionProvider>.Instance);
        var workspaceRoot = new Uri(Root);
        sessionProvider.Compose(project.ProjectInfo, workspaceRoot);

        var moduleUri = new UriBuilder(workspaceRoot) { Fragment = ModuleName }.Uri;
        var parse = new ModuleParser().Parse(new Uri(Path.Combine(Root, $"{ModuleName}.bas")), source);
        Assert.IsTrue(parse.IsSuccess);

        var resolver = WorkspaceSymbolResolver.Compose(
            workspaceRoot, [(moduleUri, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
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
    [DataRow("vba")]
    [DataRow("vb6")]
    [DataRow("basic")]
    public async Task TheStandardLibrary_IsVBA_InEveryLanguage(string id)
    {
        var result = await RunAsync("VBA.LenB(\"42\")", language: SupportedLanguages.Get(id));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "4" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task VB_IsNotTheStandardLibrary()
    {
        // `VB` is VB6's runtime library of ActiveX controls, which the platform does not model; the standard library is `VBA`.
        var result = await RunAsync("VB.LenB(\"42\")");

        Assert.AreNotEqual(ExecutionOutcome.Completed, result.Outcome);
    }
}
