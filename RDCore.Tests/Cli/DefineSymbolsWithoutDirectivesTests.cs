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
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Platform;

namespace RDCore.Tests.Cli;

/// <summary>
/// The host defining a module's symbols from a request that does not carry the module's directives
/// (<see cref="DefineSymbolsParams.PrecompilerTriviaJson"/> empty): every declaration is defined as the first branch of a <c>#If</c> block writes it.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.4 Conditional Compilation")]
public sealed class DefineSymbolsWithoutDirectivesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-define-ws");

    private static async Task<(EnvironmentSessionProvider Provider, DefineSymbolsResult Result)> DefineAsync(string source)
    {
        const string name = "Program";
        var project = new ProjectFile(Root, new RDCoreProject { Name = "Project1", Modules = [new RDCoreModule { RelativeUri = $"{name}.bas" }] });
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)) });
        var provider = new EnvironmentSessionProvider(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), fs, NullLogger<EnvironmentSessionProvider>.Instance);
        var workspaceRoot = Wire.FileUri(Root);
        provider.Compose(project.ProjectInfo, workspaceRoot);

        var moduleUri = new UriBuilder(workspaceRoot) { Fragment = name }.Uri;
        var parse = new ModuleParser().Parse(Wire.FileUri(Path.Combine(Root, $"{name}.bas")), source);
        var resolver = WorkspaceSymbolResolver.Compose(workspaceRoot, [(moduleUri, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
        var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, ModuleType.StdModule, parse, resolver).ProvideSymbols();

        var result = await new DefineSymbolsHandler(provider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance)
            .Handle(Wire.RoundTrip(new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = name,
                Symbols = SymbolDescriptorProjector.Project(symbols, moduleUri),
                Replace = true,
            }), CancellationToken.None);
        return (provider, result);
    }

    [TestMethod]
    public async Task AFieldOfATypeWrittenInEachBranch_IsDefinedOnce_AsTheFirstBranchWritesIt()
    {
        var (provider, _) = await DefineAsync(string.Join("\r\n", "Private Type Pair", "#If Mac Then", "    P As Integer", "#Else", "    P As Long", "#End If", "End Type", ""));

        Assert.IsTrue(provider.Session.Symbols.TryGetModule("Program", out var module));
        var pair = provider.Session.Symbols.MembersOf(module.Uri).OfType<VBUserDefinedTypeMemberSymbol>().Single();
        Assert.AreEqual("Integer", Assert.ContainsSingle(pair.Members).ResolvedType.Name);
    }

    [TestMethod]
    public async Task AVariableAndAProcedureOfOneName_AreANameDeclaredTwice_AndTheSecondIsReported()
    {
        var (provider, result) = await DefineAsync(string.Join("\r\n", "Private x As Long", "Private Sub x()", "End Sub", ""));

        Assert.IsTrue(provider.Session.Symbols.TryGetModule("Program", out var module));
        Assert.IsInstanceOfType<VBModuleFieldVariableMemberSymbol>(provider.Session.Symbols.MembersOf(module.Uri).Single(member => member.Name == "x"));
        CollectionAssert.Contains(result.Skipped.ToArray(), "x");
        Assert.AreEqual(0, result.MergedDefinitions, "a variable and a procedure are not one declaration");
    }
}
