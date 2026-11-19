using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Model.Symbols;
using RDCore.Tests.Cli;
using RDCore.Tests.Platform;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// What the language core states about the code of a module is about that module's code. A use of a constant, of an enumeration member or of a procedure
/// with an optional parameter evaluates an expression another module declares, and what that expression is, is said in the module that declares it.
/// </summary>
[TestClass]
public sealed class FactsOfAReferencedDeclarationTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Grid", ModuleWorkspace.ClassModule("Grid",
            "Option Explicit",
            "Public Enum GridErrors",
            "    KnownState = vbObjectError Or 127",
            "    UnknownState",
            "End Enum",
            "Public Function State() As Long",
            "    State = GridErrors.KnownState",
            "End Function")),
    ];

    private static readonly (string Name, string Source)[] Modules =
    [
        ("Settings", ModuleWorkspace.StandardModule("Settings",
            "Option Explicit",
            "Public Const Mask As Long = 1 Or 2",
            "Public Const Greeting As String = \"a\" + \"b\"",
            "Public Sub Apply(Optional ByVal flags As Long = 4 Or 8)",
            "End Sub")),
    ];

    private static readonly string Program = ModuleWorkspace.StandardModule("Program",
        "Option Explicit",
        "Public Sub Main()",
        "    Dim n As Long",
        "    Dim s As String",
        "    n = GridErrors.KnownState",
        "    n = GridErrors.UnknownState",
        "    n = Mask",
        "    s = Greeting",
        "    Settings.Apply",
        "End Sub");

    private static Task<SemanticsPayload> SemanticsAsync()
        => ModuleWorkspace.SemanticsAsync(Classes, Program, modules: Modules);

    private static ModuleSemanticsDto ModuleNamed(SemanticsPayload payload, string name)
        => payload.Modules.Single(module => SymbolUri.ModuleNameOf(module.Module) == name);

    private static Uri DocumentOf(string name, string extension)
        => Wire.FileUri(Path.Combine(ModuleWorkspace.Root, $"{name}.{extension}"));

    [TestMethod]
    [DataRow("Program", "bas")]
    [DataRow("Grid", "cls")]
    [DataRow("Settings", "bas")]
    public async Task EveryFactOfAModule_IsLocatedInItsOwnDocument(string name, string extension)
    {
        var module = ModuleNamed(await SemanticsAsync(), name);
        var document = DocumentOf(name, extension);

        var elsewhere = module.Procedures
            .SelectMany(procedure => (procedure.Runtime?.Conversions.Select(fact => fact.Location) ?? [])
                .Concat(procedure.Runtime?.Operations.Select(fact => fact.Location) ?? [])
                .Concat(procedure.Runtime?.Statements.Select(fact => fact.Location) ?? [])
                .Concat(procedure.Runtime?.Errors.Select(fact => fact.Location) ?? [])
                .Concat(procedure.Expressions.Select(fact => fact.Location)))
            .Where(location => location.Uri is not null && location.Uri != document)
            .Select(location => $"{location.Uri} L{location.Range.Start.Line}:{location.Range.Start.Character}")
            .ToArray();

        Assert.IsEmpty(elsewhere, string.Join(" | ", elsewhere));
    }

    [TestMethod]
    public async Task ABitwiseOperatorOfAnEnumerationMember_IsReportedInTheModuleThatDeclaresIt_AndNotWhereTheMemberIsUsed()
    {
        var payload = await SemanticsAsync();

        var inProgram = Findings(new BitwiseSemanticsAnalyzer(), DocumentOf("Program", "bas"), Program, ModuleNamed(payload, "Program"));
        var inGrid = Findings(new BitwiseSemanticsAnalyzer(), DocumentOf("Grid", "cls"), Classes[0].Source, ModuleNamed(payload, "Grid"));

        Assert.IsEmpty(inProgram, string.Join(" | ", inProgram.Select(finding => $"{finding.Id} L{finding.Range.Start.Line}:{finding.Range.Start.Character}")));
        Assert.AreEqual(7, inGrid.Single().Range.Start.Line);
    }

    private static AnalyzerFinding[] Findings(IModuleAnalyzer analyzer, Uri document, string source, ModuleSemanticsDto semantics)
        => [.. analyzer.Analyze(new ModuleAnalysisContext(document, new ModuleParser().Parse(document, source), semantics))];
}
