using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// A class that another class of the project implements as an interface, with code in a member (<c>RDC00411</c>).
/// </summary>
[TestClass]
public sealed class ImplementedInterfaceMemberAnalyzerTests
{
    private static readonly (string, string) Square = ("Square", ModuleWorkspace.ClassModule("Square",
        "Option Explicit",
        "Implements IShape",
        "Private Function IShape_Area() As Double", "    IShape_Area = 4", "End Function"));

    // the findings in the class module IShape, as the host models it in a workspace where Square implements it, or where nothing does.
    private static Task<AnalyzerFinding[]> ShapeAsync(bool implemented, params string[] body)
        => ShapeAsync(implemented, ModuleWorkspace.ClassModule("IShape", body));

    private static async Task<AnalyzerFinding[]> ShapeAsync(bool implemented, string source)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(implemented ? [("IShape", source), Square] : [("IShape", source)],
            ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "End Sub"));
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "IShape");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "IShape.cls"));
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. new ImplementedInterfaceMemberAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, model))];
    }

    [TestMethod]
    public async Task AnImplementedClassWithCode_IsFoundOnce_AtItsFirstMemberWithAStatement()
    {
        var findings = await ShapeAsync(true,
            "Option Explicit",
            "Public Function Name() As String",
            "    ' the name of the shape",
            "End Function",
            "Public Function Area() As Double",
            "    Area = 0",
            "End Function",
            "Public Sub Draw()",
            "    Debug.Print \"shape\"",
            "End Sub");

        CollectionAssert.AreEqual(new[] { 9 }, findings.Select(finding => finding.Range.Start.Line).ToArray(), "a class module's source has five lines of header");
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.ImplementedInterfaceMember && finding.Severity == DiagnosticSeverity.Warning));
        StringAssert.Contains(findings[0].Message, "'IShape'");
        StringAssert.Contains(findings[0].Message, "'Area'");
    }

    [TestMethod]
    public async Task AnImplementedClassWhoseMembersHaveNoStatement_IsNotFound()
        => Assert.IsEmpty(await ShapeAsync(true,
            "Option Explicit",
            "Public Function Area() As Double",
            "    ' implemented by each shape",
            "    Dim unused As Long",
            "End Function"));

    // the source with the fix's edits made, the last first so that the ranges of the others still hold.
    private static string Fixed(string source, EditFix fix)
    {
        var lines = source.Split("\r\n");
        int OffsetOf(Position position) => lines.Take(position.Line).Sum(line => line.Length + 2) + position.Character;
        foreach (var edit in fix.Edits.OrderByDescending(edit => OffsetOf(edit.Range.Start)))
        {
            source = source[..OffsetOf(edit.Range.Start)] + edit.NewText + source[OffsetOf(edit.Range.End)..];
        }

        return source;
    }

    [TestMethod]
    public async Task TheFix_RemovesTheCode_AndKeepsTheHeaders_TheCommentsAbove_AndTheAttributes()
    {
        var source = ModuleWorkspace.ClassModule("IShape",
            "Option Explicit",
            "Public Function Area() As Double",
            "Attribute Area.VB_Description = \"The area of the shape.\"",
            "    ' the area of the shape",
            "    Dim factor As Double",
            "    factor = 2",
            "",
            "    Area = factor * 2",
            "End Function",
            "Public Function Name() As String",
            "    ' the name of the shape",
            "End Function",
            "Public Sub Draw()",
            "    Debug.Print \"shape\"",
            "    ' drawn",
            "End Sub");
        var fix = Assert.ContainsSingle(await ShapeAsync(true, source)).Fixes?.Edit;
        Assert.IsNotNull(fix);
        Assert.AreEqual("Remove the code from the members of 'IShape': an object created from 'IShape' itself no longer runs it", fix.Title);

        var fixedSource = Fixed(source, fix);

        Assert.AreEqual(ModuleWorkspace.ClassModule("IShape",
            "Option Explicit",
            "Public Function Area() As Double",
            "Attribute Area.VB_Description = \"The area of the shape.\"",
            "    ' the area of the shape",
            "End Function",
            "Public Function Name() As String",
            "    ' the name of the shape",
            "End Function",
            "Public Sub Draw()",
            "End Sub"), fixedSource);
        Assert.IsEmpty(await ShapeAsync(true, fixedSource));
        var program = ModuleWorkspace.StandardModule("Program",
            "Public Sub Main()", "    Dim shape As IShape", "    Set shape = New Square", "    Debug.Print shape.Area", "End Sub");
        var square = ModuleWorkspace.ClassModule("Square",
            "Option Explicit",
            "Implements IShape",
            "Private Function IShape_Area() As Double", "    IShape_Area = 4", "End Function",
            "Private Function IShape_Name() As String", "End Function",
            "Private Sub IShape_Draw()", "End Sub");
        CollectionAssert.AreEqual(new[] { "4" }, await ModuleWorkspace.RunAsync([("IShape", fixedSource), ("Square", square)], program), "a Square runs its own code");
    }

    [TestMethod]
    [DataRow("Public Function Area() As Double: Area = 0: End Function", DisplayName = "a member on one line")]
    [DataRow("Public Function Area() As Double: Area = 0\r\nEnd Function", DisplayName = "a statement on the header's line")]
    [DataRow("Public Function Area() As Double\r\n    Area = 0: End Function", DisplayName = "a statement on the End's line")]
    [DataRow("Public Function Area() As Double\r\n#If DEBUG Then\r\n    Area = 1\r\n#End If\r\n    Area = 0\r\nEnd Function", DisplayName = "an #If in the member")]
    public async Task NoFixIsOffered_WhereTheCodeSharesALine_OrAnIfDirectiveIsInTheMember(string member)
    {
        var finding = Assert.ContainsSingle(await ShapeAsync(true, ModuleWorkspace.ClassModule("IShape", "Option Explicit", member)));

        Assert.IsNull(finding.Fixes);
    }

    [TestMethod]
    public async Task AClassWithCodeThatNothingImplements_IsNotFound()
        => Assert.IsEmpty(await ShapeAsync(false,
            "Option Explicit",
            "Public Function Area() As Double",
            "    Area = 0",
            "End Function"));
}
