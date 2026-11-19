using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11043</c>: a <c>Let</c> of a value declared as an object to a property whose value parameter is a <c>Variant</c>, to which MS-VBA passes the
/// object itself; not a value of another type, a <c>Variant</c>, or a property whose value parameter is not a <c>Variant</c>.
/// </summary>
[TestClass]
public sealed class ObjectLetToVariantPropertyAnalyzerTests
{
    private static readonly string Box = ModuleWorkspace.ClassModule("Box",
        "Private mItem As Variant",
        "Public Property Let Item(ByVal v As Variant)", "mItem = v", "End Property",
        "Public Property Let Size(ByVal v As Long)", "End Property",
        "Public Property Let At(ByVal i As Long, ByVal v As Variant)", "mItem = v", "End Property");

    // the properties the findings in Program are about, and the lines they are on: Main's statements start at line 5.
    private static async Task<(string Name, int Line)[]> FoundAsync(params string[] main)
    {
        var source = ModuleWorkspace.StandardModule("Program",
            ["Public Sub Main()", "Dim b As New Box", "Dim o As Object", "Dim other As New Box", .. main, "End Sub"]);
        var payload = await ModuleWorkspace.SemanticsAsync([("Box", Box)], source);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));

        var findings = new ObjectLetToVariantPropertyAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model)).ToArray();
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.ObjectLetToVariantProperty && finding.Severity == DiagnosticSeverity.Warning));
        return [.. findings.Select(finding => (finding.Message.Split('\'')[1], finding.Range.Start.Line))];
    }

    [TestMethod]
    public async Task AnObjectLetToAVariantProperty_IsFound()
        => CollectionAssert.AreEqual(new[] { ("Item", 6), ("Item", 7) }, await FoundAsync("Set o = New Collection", "b.Item = o", "b.Item = other"));

    [TestMethod]
    public async Task AnObjectLetToAVariantPropertyWithArguments_IsFound()
        => CollectionAssert.AreEqual(new[] { ("At", 6) }, await FoundAsync("Set o = New Collection", "b.At(1) = o"));

    [TestMethod]
    public async Task AValueThatIsNoObject_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("Dim v As Variant", "b.Item = 5", "b.Item = v"));

    [TestMethod]
    public async Task AnObjectLetToAPropertyOfAnotherType_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("Set o = New Collection", "b.Size = o"));
}
