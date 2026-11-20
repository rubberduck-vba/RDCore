using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Server.ProtocolExtensions;
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

    // a class whose default member is read with no argument.
    private static readonly string Cell = ModuleWorkspace.ClassModule("Cell",
        "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = 1", "End Property");

    // a property with a Property Let that takes a Variant, and a Property Set.
    private static readonly string Pouch = ModuleWorkspace.ClassModule("Pouch",
        "Private mItem As Variant",
        "Public Property Let Item(ByVal v As Variant)", "mItem = v", "End Property",
        "Public Property Set Item(ByVal v As Object)", "Set mItem = v", "End Property");

    // a class whose default member takes an argument.
    private static readonly string Bag = ModuleWorkspace.ClassModule("Bag",
        "Public Function Item(ByVal key As Variant) As Long", "Attribute Item.VB_UserMemId = 0", "Item = 1", "End Function");

    private static async Task<(AnalyzerFinding[] Findings, string Source)> AnalyzeAsync(string[] main)
    {
        var source = ModuleWorkspace.StandardModule("Program",
            ["Public Sub Main()", "Dim b As New Box", "Dim o As Object", "Dim other As New Box", .. main, "End Sub"]);
        var payload = await ModuleWorkspace.SemanticsAsync([("Box", Box), ("Cell", Cell), ("Pouch", Pouch), ("Bag", Bag)], source);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));

        var findings = new ObjectLetToVariantPropertyAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model)).ToArray();
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.ObjectLetToVariantProperty && finding.Severity == DiagnosticSeverity.Warning));
        return (findings, source);
    }

    // the properties the findings in Program are about, and the lines they are on: Main's statements start at line 5.
    private static async Task<(string Name, int Line)[]> FoundAsync(params string[] main)
        => [.. (await AnalyzeAsync(main)).Findings.Select(finding => (finding.Message.Split('\'')[1], finding.Range.Start.Line))];

    // the fixes of the one finding, each as its title and the statement it leaves.
    private static async Task<string[]> FixesAsync(params string[] main)
    {
        var (findings, source) = await AnalyzeAsync(main);
        var finding = Assert.ContainsSingle(findings);
        return [.. (finding.Fixes?.Edits ?? []).Select(fix => $"{fix.Title} | {fix.Edits.ApplyTo(source).Split("\r\n")[finding.Range.Start.Line].Trim()}")];
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

    [TestMethod]
    public async Task AnObjectWhoseDefaultMemberTakesNoArgument_IsFixedByNamingIt()
        => CollectionAssert.AreEqual(
            new[] { "Pass the value of the default member 'Value' | b.Item = c.Value" },
            await FixesAsync("Dim c As New Cell", "b.Item = c"),
            "the property has no Property Set");

    [TestMethod]
    [DataRow("p.Item = c", "Set p.Item = c", DisplayName = "an implicit Let")]
    [DataRow("Let p.Item = c", "Set p.Item = c", DisplayName = "an explicit Let")]
    public async Task APropertyWithAPropertySet_IsFixedBySettingTheObject(string statement, string set)
        => CollectionAssert.AreEqual(
            new[] { $"Pass the value of the default member 'Value' | {statement}.Value", $"Pass the object itself, with Set | {set}" },
            await FixesAsync("Dim c As New Cell", "Dim p As New Pouch", statement));

    [TestMethod]
    [DataRow("Set o = New Collection", "b.Item = o", DisplayName = "an Object, whose default member is known only at run time")]
    [DataRow("Dim g As New Bag", "b.Item = g", DisplayName = "a default member that takes an argument")]
    [DataRow("Dim c As New Cell", "b.Item = New Cell", DisplayName = "a New expression")]
    [DataRow("Dim c As New Cell", "b.Item = (c)", DisplayName = "a value in parentheses")]
    public async Task AValueWithNoDefaultMemberToName_HasNoFix_WhenThePropertyHasNoPropertySet(string declaration, string statement)
        => Assert.IsEmpty(await FixesAsync(declaration, statement));
}
