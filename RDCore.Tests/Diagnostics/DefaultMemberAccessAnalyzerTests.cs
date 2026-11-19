using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// A default member called without being named, as the host states it: an object used as a value or assigned one (<c>RDC00119</c>, <c>RDC00120</c> for an
/// <c>Object</c>), an argument list given to an object (<c>RDC00121</c>, <c>RDC00122</c>), a dictionary access (<c>RDC00123</c>, <c>RDC00124</c>), and an
/// object assigned to an object without <c>Set</c> (<c>RDC00125</c>).
/// </summary>
[TestClass]
public sealed class DefaultMemberAccessAnalyzerTests
{
    private static readonly string[] Procedures =
    [
        "ReadAnObject|Dim h As New Holder|x = h",
        "ReadAMemberThatReturnsAnObject|Dim o As New Outer|x = o.Inner",
        "PassAnObject|Dim h As New Holder|Take h",
        "AssignAnObject|Dim h As New Holder|h = 5",
        "AssignAnElementThatHoldsAnObject|Dim a(1) As Holder|Set a(1) = New Holder|a(1) = 5",
        "ReadAnUnboundObject|Dim v As Object|Set v = New Holder|x = v",
        "AssignAnUnboundObject|Dim v As Object|Set v = New Holder|v = 5",
        "AssignAnElementThatHoldsAnUnboundObject|Dim a(1) As Object|Set a(1) = New Holder|a(1) = 5",
        "ReadAVariant|Dim v As Variant|Set v = New Holder|x = v",
        "NameTheMember|Dim h As New Holder|x = h.Value",
        "IndexAnObject|Dim b As New Bag|x = b(1)",
        "IndexAnUnboundObject|Dim v As Object|Set v = New Bag|x = v(1)",
        "NameTheIndexedMember|Dim b As New Bag|x = b.Item(1)",
        "IndexAnObjectWithAnEmptyList|Dim op As New Opt|x = op()",
        "AssignAnIndexedObject|Dim bx As New Box|bx(1) = 5",
        "AssignAnIndexedUnboundObject|Dim v As Object|Set v = New Box|v(1) = 5",
        "AssignTheIndexedMember|Dim bx As New Box|bx.Item(1) = 5",
        "BangAnObject|Dim b As New Bag|x = b!key",
        "BangAnUnboundObject|Dim v As Object|Set v = New Bag|x = v!key",
        "LetAnObjectToAnObject|Dim h As New Holder|Dim h2 As New Holder|Let h = h2",
        "LetAnUnboundObjectToAnObject|Dim v As Object|Set v = New Holder|Dim h As New Holder|h = v",
        "LetAnObjectToAnElement|Dim a(1) As Holder|Set a(1) = New Holder|Dim h As New Holder|a(1) = h",
        "SetAnObject|Dim h As Holder|Dim h2 As New Holder|Set h = h2",
        "AssignTheDefaultMemberByName|Dim h As New Holder|h.Value = 5",
        "AssignTheDefaultMemberByNameInAWith|Dim h As New Holder|With h|.Value = 5|End With",
        "LetAnObjectToTheDefaultMemberByName|Dim h As New Holder|Dim h2 As New Holder|h.Value = h2",
        // in the next two, the statement raises 13 before the object is read, so the runtime pass does not read it: the static facts state it. The runtime
        // pass states the statements after an Error statement as well (the third).
        "ReadAnObjectAfterAnError|Dim h As New Holder|x = CLng(\"abc\") + h",
        "ReadAnUnboundObjectAfterAnError|Dim v As Object|Set v = New Holder|x = CLng(\"abc\") + v",
        "ReadAnObjectAfterAnErrorStatement|Dim h As New Holder|Error 5|x = h",
        "ReadAnObjectOfAClassWithNoDefaultMember|Dim p As New Plain|x = p",
        "AssignWhatAPropertyReturns|Dim o As New Outer|o.Inner = 5",
        "LetWhatAPropertyReturns|Dim o As New Outer|Let o.Inner = 6",
        "AssignWhatAPropertyReturnsGivenNoArgument|Dim o As New Outer|o.Inner() = 8",
        "AssignWhatAFunctionReturns|Dim o As New Outer|o.Made = 5",
        "AssignTheObjectAPropertyReturns|Dim o As New Outer|o.Unbound = 5",
        "AssignAnArgumentListToWhatAPropertyReturns|Dim o As New Outer|o.Boxed(1) = 5",
        "LetAnObjectToAnUnboundObject|Dim o As Object|Set o = New Holder|Dim h As New Holder|o = h",
        "AssignAnObjectWithNoDefaultMember|Dim p As New Plain|p = 5",
        "AssignAnObjectWhoseDefaultMemberIsAFunction|Dim op As New Opt|op = 5",
        // the runtime pass states the statements after an error as well.
        "AssignAnObjectAfterAnError|Dim h As New Holder|Error 5|h = 5",
        "LetAnObjectToAnObjectAfterAnError|Dim h As New Holder|Dim h2 As New Holder|Error 5|h = h2",
        // an object that is Nothing raises 91 before the runtime pass finds its default member: the static facts state what the Let assigns.
        "AssignNothing|Dim h As Holder|h = 5",
        "LetAnObjectToNothing|Dim h As Holder|Dim h2 As New Holder|h = h2",
        "AssignNothingOfAClassWithNoDefaultMember|Dim p As Plain|p = 5",
        "LetAnObjectToAnUnboundNothing|Dim o As Object|Dim h As New Holder|o = h",
        "LetAnUnboundObjectToAnUnboundNothing|Dim o As Object|Dim v As Object|o = v",
        // the runtime pass has no value of a parameter, only an assumed one: the static facts state what a Let to an object parameter assigns.
        "AssignAByValParameter(ByVal h As Holder)|h = 5",
        "AssignAByRefParameter(ByRef h As Holder)|h = 5",
        "AssignAByValParameterOfAClassWithNoDefaultMember(ByVal p As Plain)|p = 5",
        "AssignAByRefParameterOfAClassWithNoDefaultMember(ByRef p As Plain)|p = 5",
        "LetAnObjectToAByValParameter(ByVal h As Holder)|Dim h2 As New Holder|h = h2",
        "LetAnObjectToAByRefParameter(ByRef h As Holder)|Dim h2 As New Holder|h = h2",
        "LetAnObjectToAByValParameterOfAClassWithNoDefaultMember(ByVal p As Plain)|Dim h2 As New Holder|p = h2",
        "LetAnObjectToAByRefParameterOfAClassWithNoDefaultMember(ByRef p As Plain)|Dim h2 As New Holder|p = h2",
    ];

    private static string _source = string.Empty;
    private static ModuleSemanticsDto? _model;

    // one workspace for every test of the class: the host analyzes it once.
    [ClassInitialize]
    public static async Task AnalyzeTheWorkspaceAsync(TestContext _)
    {
        var holder = ModuleWorkspace.ClassModule("Holder", "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = 1", "End Property",
            "Public Property Let Value(ByVal v As Long)", "Attribute Value.VB_UserMemId = 0", "End Property");
        var bag = ModuleWorkspace.ClassModule("Bag", "Public Function Item(ByVal key As Variant) As Long", "Attribute Item.VB_UserMemId = 0", "Item = 1", "End Function");
        var outer = ModuleWorkspace.ClassModule("Outer", "Public Property Get Inner() As Holder", "Set Inner = New Holder", "End Property",
            "Public Function Made() As Holder", "Set Made = New Holder", "End Function",
            "Public Property Get Unbound() As Object", "Set Unbound = New Holder", "End Property",
            "Public Property Get Boxed() As Box", "Set Boxed = New Box", "End Property");
        var box = ModuleWorkspace.ClassModule("Box", "Public Property Get Item(ByVal key As Variant) As Long", "Attribute Item.VB_UserMemId = 0", "Item = 1", "End Property",
            "Public Property Let Item(ByVal key As Variant, ByVal v As Long)", "End Property");
        var opt = ModuleWorkspace.ClassModule("Opt", "Public Function Item(Optional ByVal key As Variant) As Long", "Attribute Item.VB_UserMemId = 0", "Item = 1", "End Function");
        var plain = ModuleWorkspace.ClassModule("Plain", "Public Tag As Long");

        var lines = new List<string> { "Option Explicit" };
        foreach (var procedure in Procedures)
        {
            var parts = procedure.Split('|');
            // a procedure that takes parameters is written with them.
            lines.Add(parts[0].Contains('(', StringComparison.Ordinal) ? $"Public Sub {parts[0]}" : $"Public Sub {parts[0]}()");
            lines.Add("    Dim x As Long");
            lines.AddRange(parts.Skip(1).Select(line => "    " + line));
            lines.Add("End Sub");
        }
        lines.AddRange(["Public Sub AssignAParameter(ByVal p As Holder)", "    p = 5", "End Sub", "Public Sub Take(ByVal n As Long)", "End Sub"]);
        var program = ModuleWorkspace.StandardModule("Program", [.. lines]);
        var payload = await ModuleWorkspace.SemanticsAsync(
            [("Holder", holder), ("Bag", bag), ("Outer", outer), ("Box", box), ("Opt", opt), ("Plain", plain)], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        Assert.IsEmpty(model.Procedures.SelectMany(procedure => procedure.CompileErrors), string.Join("; ", model.Procedures.SelectMany(p => p.CompileErrors).Select(e => e.Description)));
        (_source, _model) = (program, model);
    }

    // each finding, as the procedure it is in and the code it marks.
    private static string[] Findings(IModuleAnalyzer analyzer, RDCoreDiagnosticId id, DiagnosticSeverity severity)
    {
        var (source, model) = (_source, _model ?? throw new InvalidOperationException("the workspace is analyzed once, before the tests"));
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        var parsed = new ModuleParser().Parse(uri, source);
        var findings = analyzer.Analyze(new ModuleAnalysisContext(uri, parsed, model)).OrderBy(finding => finding.Range.Start).ToArray();
        Assert.IsTrue(findings.All(finding => finding.Id == id && finding.Severity == severity), string.Join("; ", findings.Select(finding => finding.Message)));

        var lines = source.Split("\r\n");
        return [.. findings.Select(finding =>
        {
            var procedure = lines.Take(finding.Range.Start.Line + 1).Last(line => line.StartsWith("Public Sub ", StringComparison.Ordinal))["Public Sub ".Length..].Split('(')[0];
            var line = lines[finding.Range.Start.Line];
            var end = finding.Range.End.Line == finding.Range.Start.Line ? finding.Range.End.Character : line.Length;
            return $"{procedure}: {line[finding.Range.Start.Character..end]}";
        })];
    }

    [TestMethod]
    public void AnObjectUsedAsAValue_OrAssignedOne_CallsItsDefaultMember()
        => Assert.AreEqual(
            string.Join(Environment.NewLine,
                "ReadAnObject: h", "ReadAMemberThatReturnsAnObject: o.Inner", "PassAnObject: h", "AssignAnObject: h", "AssignAnElementThatHoldsAnObject: a(1)",
                "LetAnObjectToTheDefaultMemberByName: h2", "ReadAnObjectAfterAnError: h", "ReadAnObjectAfterAnErrorStatement: h",
                "AssignWhatAPropertyReturns: o.Inner", "LetWhatAPropertyReturns: o.Inner", "AssignWhatAPropertyReturnsGivenNoArgument: o.Inner()",
                "AssignWhatAFunctionReturns: o.Made", "AssignAnObjectAfterAnError: h", "AssignNothing: h", "AssignAByValParameter: h",
                "AssignAByRefParameter: h", "AssignAParameter: p"),
            string.Join(Environment.NewLine,
                Findings(new ImplicitDefaultMemberAccessAnalyzer(), RDCoreDiagnosticId.ImplicitDefaultMemberAccess, DiagnosticSeverity.Information)),
            "what a member returns included, and not an Object, a Variant, a member named (the default member included), an object assigned to an object, "
            + "nor a class with no default member to assign or a function; Nothing and a parameter too");

    [TestMethod]
    public void AnObjectUsedAsAValue_HasADefaultMemberKnownOnlyAtRunTime()
        => CollectionAssert.AreEqual(
            new[]
            {
                "ReadAnUnboundObject: v", "AssignAnUnboundObject: v", "AssignAnElementThatHoldsAnUnboundObject: a(1)", "ReadAnUnboundObjectAfterAnError: v",
                "AssignTheObjectAPropertyReturns: o.Unbound",
            },
            Findings(new ImplicitUnboundDefaultMemberAccessAnalyzer(), RDCoreDiagnosticId.ImplicitUnboundDefaultMemberAccess, DiagnosticSeverity.Warning),
            "not a Variant, which nothing says holds an object, nor the Object assigned to an object");

    [TestMethod]
    public void AnArgumentListGivenToAnObject_GoesToItsDefaultMember()
        => CollectionAssert.AreEqual(
            new[]
            {
                "IndexAnObject: b(1)", "IndexAnObjectWithAnEmptyList: op()", "AssignAnIndexedObject: bx(1)",
                "AssignAnArgumentListToWhatAPropertyReturns: o.Boxed(1)",
            },
            Findings(new IndexedDefaultMemberAccessAnalyzer(), RDCoreDiagnosticId.IndexedDefaultMemberAccess, DiagnosticSeverity.Information),
            "read or assigned, an empty list too, and not a member named");

    [TestMethod]
    public void AnArgumentListGivenToAnObject_GoesToADefaultMemberKnownOnlyAtRunTime()
        => CollectionAssert.AreEqual(
            new[] { "IndexAnUnboundObject: v(1)", "AssignAnIndexedUnboundObject: v(1)" },
            Findings(new IndexedUnboundDefaultMemberAccessAnalyzer(), RDCoreDiagnosticId.IndexedUnboundDefaultMemberAccess, DiagnosticSeverity.Warning));

    [TestMethod]
    public void ABangName_GoesToTheDefaultMember()
        => CollectionAssert.AreEqual(
            new[] { "BangAnObject: b!key" },
            Findings(new BangNotationAnalyzer(), RDCoreDiagnosticId.BangNotation, DiagnosticSeverity.Information));

    [TestMethod]
    public void ABangName_GoesToADefaultMemberKnownOnlyAtRunTime()
        => CollectionAssert.AreEqual(
            new[] { "BangAnUnboundObject: v!key" },
            Findings(new UnboundBangNotationAnalyzer(), RDCoreDiagnosticId.UnboundBangNotation, DiagnosticSeverity.Warning));

    [TestMethod]
    public void AnObjectAssignedToAnObjectWithoutSet_IsSuspicious()
        => Assert.AreEqual(
            string.Join(Environment.NewLine,
                "LetAnObjectToAnObject: Let h = h2", "LetAnUnboundObjectToAnObject: h = v", "LetAnObjectToAnElement: a(1) = h",
                "LetAnObjectToAnUnboundObject: o = h", "LetAnObjectToAnObjectAfterAnError: h = h2", "LetAnObjectToNothing: h = h2",
                "LetAnObjectToAnUnboundNothing: o = h", "LetAnUnboundObjectToAnUnboundNothing: o = v", "LetAnObjectToAByValParameter: h = h2",
                "LetAnObjectToAByRefParameter: h = h2"),
            string.Join(Environment.NewLine,
                Findings(new SuspiciousLetAssignmentAnalyzer(), RDCoreDiagnosticId.SuspiciousLetAssignment, DiagnosticSeverity.Warning)),
            "not a Set; to an Object too, to Nothing, and to a parameter");
}
