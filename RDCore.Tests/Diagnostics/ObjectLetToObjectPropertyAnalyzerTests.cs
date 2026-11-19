using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11047</c>: a <c>Let</c> of a value declared as an object to a property whose value parameter is a class or <c>Object</c>, to which MS-VBA passes
/// the object itself; not a <c>Set</c>, a <c>Variant</c>, or a property whose value parameter is not a class or <c>Object</c>.
/// </summary>
[TestClass]
public sealed class ObjectLetToObjectPropertyAnalyzerTests
{
    private static readonly string Holder = ModuleWorkspace.ClassModule("Holder",
        "Private mItem As Object",
        "Public Property Let PObj(ByVal v As Object)", "Set mItem = v", "End Property",
        "Public Property Let PCol(ByVal v As Collection)", "Set mItem = v", "End Property",
        "Public Property Let PVar(ByVal v As Variant)", "End Property",
        "Public Property Let PAt(ByVal i As Long, ByVal v As Object)", "Set mItem = v", "End Property",
        "Public Property Set PSetOnly(ByVal v As Object)", "Set mItem = v", "End Property");

    // the properties the findings in Program are about, and the lines they are on: Main's statements start at line 5.
    private static async Task<(string Name, int Line)[]> FoundAsync(params string[] main)
    {
        var source = ModuleWorkspace.StandardModule("Program",
            ["Public Sub Main()", "Dim h As New Holder", "Dim o As Object", "Dim c As New Collection", .. main, "End Sub"]);
        var payload = await ModuleWorkspace.SemanticsAsync([("Holder", Holder)], source);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));

        var findings = new ObjectLetToObjectPropertyAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model)).ToArray();
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.ObjectLetToObjectProperty && finding.Severity == DiagnosticSeverity.Warning));
        return [.. findings.Select(finding => (finding.Message.Split('\'')[1], finding.Range.Start.Line))];
    }

    [TestMethod]
    public async Task AnObjectLetToAnObjectOrClassProperty_IsFound()
        => CollectionAssert.AreEqual(new[] { ("PObj", 6), ("PObj", 7), ("PCol", 8) },
            await FoundAsync("Set o = New Collection", "h.PObj = o", "h.PObj = c", "h.PCol = c"));

    [TestMethod]
    public async Task AnObjectLetToAnObjectPropertyWithArguments_IsFound()
        => CollectionAssert.AreEqual(new[] { ("PAt", 6) }, await FoundAsync("Set o = New Collection", "h.PAt(1) = o"));

    [TestMethod]
    public async Task ASet_AVariant_OrAVariantProperty_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("Dim v As Variant", "Set v = c", "Set h.PSetOnly = c", "h.PObj = v", "h.PVar = c"));
}
