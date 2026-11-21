using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC01013</c> on a parameter of an interface member or of an event: reported where it has implementations or handlers and none refers to its own
/// parameter at that position.
/// </summary>
[TestClass]
public sealed class ParameterUnusedByBoundMembersTests
{
    private const string Program = "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n";

    private static string Shape(string body)
        => ModuleWorkspace.ClassModule("Circle", "Implements IShape", "Private Sub IShape_Area(ByVal size As Double)", body, "End Sub");

    private static async Task<AnalyzerFinding[]> FindingsAsync(string name, params (string Name, string Source)[] classes)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(classes, Program);
        var model = payload.Modules.Single(module => SymbolUri.ModuleNameOf(module.Module) == name);
        var source = classes.Single(module => module.Name == name).Source;
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, $"{name}.cls"));
        return [.. new ParameterNotUsedAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model))];
    }

    private static readonly (string, string) IShape = ("IShape", ModuleWorkspace.ClassModule("IShape", "Public Sub Area(ByVal size As Double)", "End Sub"));

    [TestMethod]
    public async Task AParameterOfAnInterfaceMember_ThatNoImplementationRefersTo_IsReported_AndRemovedFromThemAll()
    {
        var finding = Assert.ContainsSingle(await FindingsAsync("IShape", IShape, ("Circle", Shape("")),
            ("Square", ModuleWorkspace.ClassModule("Square", "Implements IShape", "Private Sub IShape_Area(ByVal factor As Double)", "End Sub"))));

        Assert.AreEqual(RDCoreDiagnosticId.ParameterNotUsed.ToCode(), finding.Code);
        Assert.AreEqual(DiagnosticSeverity.Information, finding.Severity);
        StringAssert.Contains(finding.Message, "'size'");
        StringAssert.Contains(finding.Message, "'Area'");
        Assert.AreEqual(RefactoringKinds.RemoveParameter, finding.Fixes?.Refactoring?.Kind);
    }

    [TestMethod]
    public async Task AParameterOfAnInterfaceMember_ThatAnImplementationRefersTo_IsNotReported()
        => Assert.IsEmpty(await FindingsAsync("IShape", IShape, ("Circle", Shape("    Debug.Print size")),
            ("Square", ModuleWorkspace.ClassModule("Square", "Implements IShape", "Private Sub IShape_Area(ByVal factor As Double)", "End Sub"))));

    [TestMethod]
    public async Task AParameterOfAnExposedInterface_IsNotReported_AReferencingProjectMayImplementIt()
        => Assert.IsEmpty(await FindingsAsync("IShape",
            ("IShape", ModuleWorkspace.ClassModule("IShape", "Attribute VB_Exposed = True", "Public Sub Area(ByVal size As Double)", "End Sub")),
            ("Circle", Shape(""))));

    [TestMethod]
    public async Task AParameterOfAnEvent_ThatNoHandlerRefersTo_IsReported_WithNoFix()
    {
        var finding = Assert.ContainsSingle(await FindingsAsync("Field", Field, Watcher("")));

        StringAssert.Contains(finding.Message, "'oldValue'");
        StringAssert.Contains(finding.Message, "'Changed'");
        Assert.IsNull(finding.Fixes);
    }

    [TestMethod]
    public async Task AParameterOfAnEvent_ThatAHandlerRefersTo_OrOfOneNoHandlerHandles_IsNotReported()
    {
        Assert.IsEmpty(await FindingsAsync("Field", Field, Watcher("    Debug.Print oldValue")));
        Assert.IsEmpty(await FindingsAsync("Field", Field));
    }

    private static readonly (string, string) Field = ("Field", ModuleWorkspace.ClassModule("Field",
        "Public Event Changed(ByVal oldValue As Long)", "Public Sub Touch()", "    RaiseEvent Changed(1)", "End Sub"));

    private static (string, string) Watcher(string body) => ("Watcher", ModuleWorkspace.ClassModule("Watcher",
        "Private WithEvents source As Field", "Private Sub source_Changed(ByVal oldValue As Long)", body, "End Sub"));
}
