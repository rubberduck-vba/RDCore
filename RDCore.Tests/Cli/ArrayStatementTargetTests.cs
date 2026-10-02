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
/// <strong>MS-VBAL §5.4.3.3-4</strong> <c>ReDim</c> and <c>Erase</c> of an array that is the field of an object, which is a member access and an expression: the
/// array is read from it, and the new one is written back through it.
/// </summary>
/// <remarks>
/// A class <c>Box</c> with a dynamic <c>Items()</c>, a fixed-size <c>Fixed(1 To 3)</c> and a <c>Loose</c> Variant, and a <c>Program</c> whose <c>Main</c> has a
/// <c>Box</c>, <c>b</c>, and runs the body of the test.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim Statement")]
[TestCategory("MS-VBAL 5.4.3.4 Erase Statement")]
public sealed class ArrayStatementTargetTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-array-targets-ws");

    private const string BoxSource =
        "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Box\"\r\n"
        + "Public Items() As Long\r\nPublic Fixed(1 To 3) As Long\r\nPublic Loose As Variant\r\nPublic Count As Long\r\n";

    private static Task<string[]> RunAsync(params string[] body) => RunProgramAsync(string.Empty, string.Empty, body);

    // `moduleLevel` is what Program declares before its procedures, and `procedures` the procedures it has besides Main, whose body is `body`.
    internal static async Task<string[]> RunProgramAsync(string moduleLevel, string procedures, params string[] body)
    {
        var programSource = $"Attribute VB_Name = \"Program\"\r\n{moduleLevel}\r\nPublic Sub Main()\r\nDim b As Box\r\nSet b = New Box\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n{procedures}\r\n";
        (string Name, string Extension, ModuleType Type, string Source)[] modules =
        [
            ("Box", "cls", ModuleType.ClassModule, BoxSource),
            ("Program", "bas", ModuleType.StdModule, programSource),
        ];

        var project = new ProjectFile(Root, new RDCoreProject
        {
            Name = "Project1",
            Modules = [.. modules.Select(module => new RDCoreModule { RelativeUri = $"{module.Name}.{module.Extension}" })],
        });
        var files = new Dictionary<string, MockFileData> { [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)) };
        foreach (var module in modules)
        {
            files[Path.Combine(Root, $"{module.Name}.{module.Extension}")] = new(module.Source);
        }

        var sessionProvider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new MockFileSystem(files), NullLogger<EnvironmentSessionProvider>.Instance);
        var workspaceRoot = new Uri(Root);
        sessionProvider.Compose(project.ProjectInfo, workspaceRoot);

        var parsed = modules.Select(module =>
        {
            var parse = new ModuleParser().Parse(new Uri(Path.Combine(Root, $"{module.Name}.{module.Extension}")), module.Source);
            Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
            return (Module: module, Uri: new UriBuilder(workspaceRoot) { Fragment = module.Name }.Uri, Parse: parse);
        }).ToArray();

        var resolver = WorkspaceSymbolResolver.Compose(
            workspaceRoot, parsed.Select(module => (module.Uri, module.Module.Type, module.Parse)), new IntrinsicSymbolResolver());

        foreach (var module in parsed)
        {
            var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, module.Uri, module.Module.Type, module.Parse, resolver, withImplicitDeclarations: true).ProvideSymbols();
            var defined = await new DefineSymbolsHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance)
                .Handle(new DefineSymbolsParams
                {
                    WorkspaceRoot = workspaceRoot,
                    ModuleUri = module.Uri,
                    ModuleName = module.Module.Name,
                    Symbols = SymbolDescriptorProjector.Project(symbols, module.Uri),
                    Directives = module.Parse.SyntaxTree.GetModuleDirectives(),
                    ParseResultJson = PlatformJson.Serialize(module.Parse),
                    Replace = true,
                }, CancellationToken.None);

            Assert.IsEmpty(defined.CodeErrors, string.Join("; ", defined.CodeErrors));
        }

        var program = parsed.Single(module => module.Module.Name == "Program");
        var result = await new HostExecuteHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<HostExecuteHandler>.Instance)
            .Handle(new HostExecuteParams
            {
                Json = PlatformJson.Serialize(new HostExecutePayload(program.Uri, program.Parse)),
                ModuleName = "Program",
                EntryPoint = "Main",
            }, CancellationToken.None);

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, $"{result.ErrorMessage} {string.Join("; ", result.Diagnostics ?? [])}");
        return [.. result.Output.Select(line => line.Trim())];
    }

    [TestMethod]
    public async Task ReDim_OfAMembersArray_GivesTheFieldItsDimensions()
        => CollectionAssert.AreEqual(new[] { "3", "7" },
            await RunAsync("ReDim b.Items(1 To 3)", "b.Items(2) = 7", "Debug.Print UBound(b.Items)", "Debug.Print b.Items(2)"));

    [TestMethod]
    public async Task ReDimPreserve_OfAMembersArray_KeepsWhatItHolds()
        => CollectionAssert.AreEqual(new[] { "5", "7" },
            await RunAsync("ReDim b.Items(1 To 3)", "b.Items(2) = 7", "ReDim Preserve b.Items(1 To 5)", "Debug.Print UBound(b.Items)", "Debug.Print b.Items(2)"));

    [TestMethod]
    public async Task ReDim_WithoutPreserve_OfAMembersArray_StartsOverFromDefaults()
        => CollectionAssert.AreEqual(new[] { "0" },
            await RunAsync("ReDim b.Items(1 To 3)", "b.Items(2) = 7", "ReDim b.Items(1 To 3)", "Debug.Print b.Items(2)"));

    [TestMethod]
    public async Task ReDim_OfAMembersArray_InAWithBlock_IsTheWithTargets()
        => CollectionAssert.AreEqual(new[] { "2", "4" },
            await RunAsync("With b", "ReDim .Items(1 To 2)", ".Items(1) = 4", "End With", "Debug.Print UBound(b.Items)", "Debug.Print b.Items(1)"));

    [TestMethod]
    public async Task ReDim_OfAMembersVariant_MakesItAnArray()
        => CollectionAssert.AreEqual(new[] { "9" },
            await RunAsync("ReDim b.Loose(1 To 2)", "b.Loose(1) = 9", "Debug.Print b.Loose(1)"));

    [TestMethod]
    public async Task ReDim_OfAMembersArray_WithBoundsThatAreExpressions_EvaluatesThem()
        => CollectionAssert.AreEqual(new[] { "4" },
            await RunAsync("Dim n As Long", "n = 4", "ReDim b.Items(1 To n)", "Debug.Print UBound(b.Items)"));

    [TestMethod]
    public async Task Erase_OfAMembersDynamicArray_TakesTheDimensionsAway()
        => CollectionAssert.AreEqual(new[] { "9" },
            await RunAsync("ReDim b.Items(1 To 3)", "Erase b.Items", "On Error Resume Next", "Debug.Print UBound(b.Items)", "Debug.Print Err.Number"));

    [TestMethod]
    public async Task Erase_OfAMembersFixedArray_ResetsItsElements_AndKeepsItsDimensions()
        => CollectionAssert.AreEqual(new[] { "0", "3" },
            await RunAsync("b.Fixed(2) = 5", "Erase b.Fixed", "Debug.Print b.Fixed(2)", "Debug.Print UBound(b.Fixed)"));

    [TestMethod]
    public async Task Erase_OfAMembersVariantArray_TakesItsDimensionsAway()
        => CollectionAssert.AreEqual(new[] { "9" },
            await RunAsync("ReDim b.Loose(1 To 2)", "Erase b.Loose", "On Error Resume Next", "Debug.Print UBound(b.Loose)", "Debug.Print Err.Number"));

    [TestMethod]
    public async Task AWholeArray_AssignedToAMembersArray_IsCopiedIntoIt()
        => CollectionAssert.AreEqual(new[] { "2", "3" },
            await RunAsync("Dim a(1 To 2) As Long", "a(1) = 3", "b.Items = a", "Debug.Print UBound(b.Items)", "Debug.Print b.Items(1)"));

    [TestMethod]
    public async Task Erase_OfAnElementThatHoldsAnArray_TakesTheDimensionsAway()
        => CollectionAssert.AreEqual(new[] { "9" },
            await RunAsync("Dim v(1 To 2) As Variant", "ReDim b.Loose(1 To 3)", "v(1) = b.Loose", "Erase v(1)",
                "On Error Resume Next", "Debug.Print UBound(v(1))", "Debug.Print Err.Number"));

    [TestMethod]
    public async Task Erase_OfSeveralArrays_IncludingAMembers_ErasesEachOfThem()
        => CollectionAssert.AreEqual(new[] { "0", "0" },
            await RunAsync("Dim a(1 To 2) As Long", "a(1) = 8", "b.Fixed(1) = 6", "Erase a, b.Fixed", "Debug.Print a(1)", "Debug.Print b.Fixed(1)"));
}
