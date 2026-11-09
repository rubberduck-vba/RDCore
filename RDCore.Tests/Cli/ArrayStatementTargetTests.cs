using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Server.Configuration;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Workspace;
using RDCore.Symbols;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.3.3-4</strong> <c>ReDim</c> and <c>Erase</c> of an array that is a member - the field of a record, or a module's variable named through the
/// module - which is a member access and an expression: the array is read from it, and the new one is written back through it.
/// </summary>
/// <remarks>
/// A record type <c>TBox</c> with a dynamic <c>Items()</c>, a fixed-size <c>Fixed(1 To 3)</c> and a <c>Loose</c> Variant, a class <c>Box</c> with a <c>Loose</c>
/// Variant and a <c>Count</c>, and a <c>Program</c> whose <c>Main</c> has a <c>TBox</c>, <c>r</c>, and a <c>Box</c>, <c>b</c>, and runs the body of the test.
/// <para>
/// 👉 A class module cannot expose an array (<c>VBC09337</c>), and an object's member cannot be re-dimensioned: MS-VBA refuses <c>ReDim b.Loose(1)</c> as a
/// compile error. Each expectation is what MS-VBA prints for the same body, recorded in Excel.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim Statement")]
[TestCategory("MS-VBAL 5.4.3.4 Erase Statement")]
public sealed class ArrayStatementTargetTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-array-targets-ws");

    private const string BoxSource =
        "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Box\"\r\n"
        + "Public Loose As Variant\r\nPublic Count As Long\r\n";

    private const string RecordType = "Private Type TBox\r\nItems() As Long\r\nFixed(1 To 3) As Long\r\nLoose As Variant\r\nEnd Type";

    private const string Mod2Source = "Attribute VB_Name = \"Mod2\"\r\nPublic Items() As Long\r\n";

    private static Task<string[]> RunAsync(params string[] body) => RunProgramAsync(string.Empty, string.Empty, body);

    // `moduleLevel` is what Program declares before its procedures, and `procedures` the procedures it has besides Main, whose body is `body`.
    internal static async Task<string[]> RunProgramAsync(string moduleLevel, string procedures, params string[] body)
    {
        var programSource = $"Attribute VB_Name = \"Program\"\r\n{RecordType}\r\n{moduleLevel}\r\nPublic Sub Main()\r\nDim b As Box\r\nSet b = New Box\r\nDim r As TBox\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n{procedures}\r\n";
        (string Name, string Extension, ModuleType Type, string Source)[] modules =
        [
            ("Box", "cls", ModuleType.ClassModule, BoxSource),
            ("Mod2", "bas", ModuleType.StdModule, Mod2Source),
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
            var defined = await new DefineSymbolsHandler(sessionProvider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance)
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
        var result = await new HostExecuteHandler(sessionProvider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<HostExecuteHandler>.Instance)
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
            await RunAsync("ReDim r.Items(1 To 3)", "r.Items(2) = 7", "Debug.Print UBound(r.Items)", "Debug.Print r.Items(2)"));

    [TestMethod]
    public async Task ReDimPreserve_OfAMembersArray_KeepsWhatItHolds()
        => CollectionAssert.AreEqual(new[] { "5", "7" },
            await RunAsync("ReDim r.Items(1 To 3)", "r.Items(2) = 7", "ReDim Preserve r.Items(1 To 5)", "Debug.Print UBound(r.Items)", "Debug.Print r.Items(2)"));

    [TestMethod]
    public async Task ReDimPreserve_OfAModulesVariable_NamedThroughTheModule_KeepsWhatItHolds()
        => CollectionAssert.AreEqual(new[] { "5", "7" },
            await RunAsync("ReDim Mod2.Items(1 To 3)", "Mod2.Items(2) = 7", "ReDim Preserve Mod2.Items(1 To 5)", "Debug.Print UBound(Mod2.Items)", "Debug.Print Mod2.Items(2)"));

    [TestMethod]
    public async Task ReDim_WithoutPreserve_OfAMembersArray_StartsOverFromDefaults()
        => CollectionAssert.AreEqual(new[] { "0" },
            await RunAsync("ReDim r.Items(1 To 3)", "r.Items(2) = 7", "ReDim r.Items(1 To 3)", "Debug.Print r.Items(2)"));

    [TestMethod]
    public async Task ReDim_OfAMembersArray_InAWithBlock_IsTheWithTargets()
        => CollectionAssert.AreEqual(new[] { "2", "4" },
            await RunAsync("With r", "ReDim .Items(1 To 2)", ".Items(1) = 4", "End With", "Debug.Print UBound(r.Items)", "Debug.Print r.Items(1)"));

    [TestMethod]
    public async Task ReDim_OfAMembersVariant_MakesItAnArray()
        => CollectionAssert.AreEqual(new[] { "9" },
            await RunAsync("ReDim r.Loose(1 To 2)", "r.Loose(1) = 9", "Debug.Print r.Loose(1)"));

    [TestMethod]
    public async Task ReDim_OfAMembersArray_WithBoundsThatAreExpressions_EvaluatesThem()
        => CollectionAssert.AreEqual(new[] { "4" },
            await RunAsync("Dim n As Long", "n = 4", "ReDim r.Items(1 To n)", "Debug.Print UBound(r.Items)"));

    // the statement that fails is skipped: only the error number is printed.
    [TestMethod]
    public async Task Erase_OfAMembersDynamicArray_TakesTheDimensionsAway()
        => CollectionAssert.AreEqual(new[] { "9" },
            await RunAsync("ReDim r.Items(1 To 3)", "Erase r.Items", "On Error Resume Next", "Debug.Print UBound(r.Items)", "Debug.Print Err.Number"));

    [TestMethod]
    public async Task Erase_OfAMembersFixedArray_ResetsItsElements_AndKeepsItsDimensions()
        => CollectionAssert.AreEqual(new[] { "0", "3" },
            await RunAsync("r.Fixed(2) = 5", "Erase r.Fixed", "Debug.Print r.Fixed(2)", "Debug.Print UBound(r.Fixed)"));

    [TestMethod]
    public async Task Erase_OfAMembersVariantArray_TakesItsDimensionsAway()
        => CollectionAssert.AreEqual(new[] { "9" },
            await RunAsync("ReDim r.Loose(1 To 2)", "Erase r.Loose", "On Error Resume Next", "Debug.Print UBound(r.Loose)", "Debug.Print Err.Number"));

    // what an object's member is read as is a value of its own: the array the Variant holds is not the one that is erased.
    [TestMethod]
    public async Task Erase_OfAnObjectsVariant_ErasesAValueOfIt_AndLeavesTheMemberAsItWas()
        => CollectionAssert.AreEqual(new[] { "1", "0" },
            await RunAsync("b.Loose = Array(1, 2)", "Erase b.Loose", "On Error Resume Next", "Debug.Print UBound(b.Loose)", "Debug.Print Err.Number"));

    [TestMethod]
    public async Task AWholeArray_AssignedToAMembersArray_IsCopiedIntoIt()
        => CollectionAssert.AreEqual(new[] { "2", "3" },
            await RunAsync("Dim a(1 To 2) As Long", "a(1) = 3", "r.Items = a", "Debug.Print UBound(r.Items)", "Debug.Print r.Items(1)"));

    [TestMethod]
    public async Task Erase_OfAnElementThatHoldsAnArray_TakesTheDimensionsAway()
        => CollectionAssert.AreEqual(new[] { "9" },
            await RunAsync("Dim v(1 To 2) As Variant", "b.Loose = Array(1, 2, 3)", "v(1) = b.Loose", "Erase v(1)",
                "On Error Resume Next", "Debug.Print UBound(v(1))", "Debug.Print Err.Number"));

    [TestMethod]
    public async Task Erase_OfSeveralArrays_IncludingAMembers_ErasesEachOfThem()
        => CollectionAssert.AreEqual(new[] { "0", "0" },
            await RunAsync("Dim a(1 To 2) As Long", "a(1) = 8", "r.Fixed(1) = 6", "Erase a, r.Fixed", "Debug.Print a(1)", "Debug.Print r.Fixed(1)"));
}
