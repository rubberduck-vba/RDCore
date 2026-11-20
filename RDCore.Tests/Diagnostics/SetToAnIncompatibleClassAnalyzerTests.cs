using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11051</c> and <c>RDC11052</c>: a <c>Set</c>, and an argument given to a parameter declared as a class, whose value is declared as a class no object
/// of which is an object of the destination's class or of a class that implements it (<strong>MS-VBAL §5.5.2.2.1</strong>, <strong>§5.4.3.9</strong>,
/// <strong>§5.3.1.11</strong>), reported as a warning at the value.
/// </summary>
[TestClass]
public sealed class SetToAnIncompatibleClassAnalyzerTests
{
    private static readonly (string, string)[] Classes =
    [
        ("IShape", ModuleWorkspace.ClassModule("IShape", "Public Sub Draw()", "End Sub")),
        ("IDrawable", ModuleWorkspace.ClassModule("IDrawable", "Public Sub Paint()", "End Sub")),
        ("Ring", ModuleWorkspace.ClassModule("Ring", "Implements IShape", "Private Sub IShape_Draw()", "End Sub")),
        ("Square", ModuleWorkspace.ClassModule("Square", "Implements IShape", "Implements IDrawable",
            "Private Sub IShape_Draw()", "End Sub", "Private Sub IDrawable_Paint()", "End Sub")),
        ("Account", ModuleWorkspace.ClassModule("Account", "Public Balance As Currency")),
        ("IBase", ModuleWorkspace.ClassModule("IBase", "Public Sub Touch()", "End Sub")),
        ("IMiddle", ModuleWorkspace.ClassModule("IMiddle", "Implements IBase", "Private Sub IBase_Touch()", "End Sub")),
        ("Widget", ModuleWorkspace.ClassModule("Widget", "Implements IMiddle", "Private Sub IMiddle_Touch()", "End Sub")),
    ];

    private const string Procedures = "Public Sub Takes(ByVal s As IShape)\r\nEnd Sub\r\nPublic Sub Keeps(ByRef c As Ring)\r\nEnd Sub\r\n";

    private static string Program(string declaration, string statement)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{declaration}\r\n{statement}\r\nEnd Sub\r\n{Procedures}";

    private static async Task<ModuleSemanticsDto> ModelAsync(string program)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(Classes, program);
        return payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
    }

    private static async Task<AnalyzerFinding[]> FindingsAsync(IModuleAnalyzer analyzer, string declaration, string statement)
    {
        var program = Program(declaration, statement);
        var model = await ModelAsync(program);
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("Dim c As Ring", "Set c = New Ring", ClassRelation.Same, DisplayName = "the same class")]
    [DataRow("Dim s As IShape", "Set s = New Ring", ClassRelation.Implements, DisplayName = "a class that implements the target's")]
    [DataRow("Dim b As IBase", "Set b = New Widget", ClassRelation.Implements, DisplayName = "a class that implements it through another")]
    [DataRow("Dim s As IShape, c As Ring", "Set c = s", ClassRelation.ImplementedBy, DisplayName = "an interface the target's class implements")]
    [DataRow("Dim s As IShape, d As IDrawable", "Set d = s", ClassRelation.SharedImplementer, DisplayName = "an interface another class implements too")]
    [DataRow("Dim s As IShape", "Set s = New Account", ClassRelation.Unrelated, DisplayName = "a class unrelated to the target's")]
    [DataRow("Dim a As Account", "Takes a", ClassRelation.Unrelated, DisplayName = "an argument unrelated to the parameter's class")]
    [DataRow("Dim n As Long", "Takes s:=New Ring", ClassRelation.Implements, DisplayName = "a named argument")]
    public async Task AValueSetToAClassOfTheWorkspace_StatesHowItsClassRelatesToTheDestinations(string declaration, string statement, ClassRelation relation)
    {
        var model = await ModelAsync(Program(declaration, statement));

        var coercion = Assert.ContainsSingle(
            model.Procedures.SelectMany(procedure => procedure.Expressions).Select(fact => fact.SetCoercion).OfType<SetCoercionFact>());
        Assert.AreEqual(relation, coercion.Relation);
    }

    [TestMethod]
    [DataRow("Dim o As Object", "Set o = New Account", DisplayName = "an Object target")]
    [DataRow("Dim s As IShape", "Set s = Nothing", DisplayName = "Nothing")]
    [DataRow("Dim s As IShape, col As New Collection", "Set s = col", DisplayName = "an object of a library's class")]
    [DataRow("Dim c As Ring", "Keeps c", DisplayName = "a variable passed by reference")]
    public async Task ASetThatIsNotBetweenTwoClassesOfTheWorkspace_StatesNoSetCoercion(string declaration, string statement)
    {
        var model = await ModelAsync(Program(declaration, statement));

        Assert.IsFalse(model.Procedures.SelectMany(procedure => procedure.Expressions).Any(fact => fact.SetCoercion is not null));
    }

    [TestMethod]
    [DataRow("Dim s As IShape", "Set s = New Account", DisplayName = "a New of an unrelated class")]
    [DataRow("Dim s As IShape, a As Account", "Set s = a", DisplayName = "a variable of an unrelated class")]
    public async Task ASetToAnUnrelatedClass_IsAWarningNamingBothClasses(string declaration, string statement)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(new SetToAnIncompatibleClassAnalyzer(), declaration, statement));

        Assert.AreEqual(RDCoreDiagnosticId.SetToAnIncompatibleClass.ToCode(), finding.Code);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, "'Account' is an object of class 'IShape'");
    }

    [TestMethod]
    [DataRow("Dim s As IShape", "Set s = New Ring", DisplayName = "a class that implements the target's")]
    [DataRow("Dim s As IShape, c As Ring", "Set c = s", DisplayName = "an interface the target's class implements")]
    [DataRow("Dim s As IShape, d As IDrawable", "Set d = s", DisplayName = "an interface another class implements too")]
    [DataRow("Dim s As IShape, col As New Collection", "Set s = col", DisplayName = "an object of a library's class")]
    [DataRow("Dim a As Account", "Takes a", DisplayName = "an argument")]
    public async Task ASetThatCanSucceed_OrAnArgument_IsNotReportedAsRDC11051(string declaration, string statement)
        => Assert.IsEmpty(await FindingsAsync(new SetToAnIncompatibleClassAnalyzer(), declaration, statement));

    [TestMethod]
    [DataRow("Dim a As Account", "Takes a", DisplayName = "a variable")]
    [DataRow("Dim a As Account", "Takes New Account", DisplayName = "a New")]
    [DataRow("Dim a As Account", "Takes s:=a", DisplayName = "a named argument")]
    public async Task AnArgumentOfAnUnrelatedClass_IsAWarningNamingBothClasses(string declaration, string statement)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(new ArgumentOfAnIncompatibleClassAnalyzer(), declaration, statement));

        Assert.AreEqual(RDCoreDiagnosticId.ArgumentOfAnIncompatibleClass.ToCode(), finding.Code);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, "'Account' is an object of the parameter's class 'IShape'");
    }

    [TestMethod]
    [DataRow("Dim c As Ring", "Takes c", DisplayName = "a class that implements the parameter's")]
    [DataRow("Dim c As Ring", "Keeps c", DisplayName = "a variable passed by reference")]
    [DataRow("Dim s As IShape", "Set s = New Account", DisplayName = "a Set")]
    public async Task AnArgumentThatCanSucceed_OrASet_IsNotReportedAsRDC11052(string declaration, string statement)
        => Assert.IsEmpty(await FindingsAsync(new ArgumentOfAnIncompatibleClassAnalyzer(), declaration, statement));
}
