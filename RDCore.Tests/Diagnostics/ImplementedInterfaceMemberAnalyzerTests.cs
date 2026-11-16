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
    private static async Task<AnalyzerFinding[]> ShapeAsync(bool implemented, params string[] body)
    {
        var source = ModuleWorkspace.ClassModule("IShape", body);
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

    [TestMethod]
    public async Task AClassWithCodeThatNothingImplements_IsNotFound()
        => Assert.IsEmpty(await ShapeAsync(false,
            "Option Explicit",
            "Public Function Area() As Double",
            "    Area = 0",
            "End Function"));
}
