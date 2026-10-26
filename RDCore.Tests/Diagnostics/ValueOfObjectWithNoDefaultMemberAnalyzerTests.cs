using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11015</c>: an object of a class that has no default member, where a value is expected - which MS-VBA compiles, to raise 438 when it runs (91 when
/// it is <c>Nothing</c>; Excel, <c>oracle\s46</c> F16a-c), and MS-VBAL §5.5.1.1 makes a compile error.
/// </summary>
[TestClass]
public sealed class ValueOfObjectWithNoDefaultMemberAnalyzerTests
{
    private static readonly (string, string) Plain = ("Plain", ModuleWorkspace.ClassModule("Plain", "Public Tag As Long"));
    private static readonly (string, string) Widget = ("Widget", ModuleWorkspace.ClassModule("Widget",
        "Public Function Value() As Long", "Attribute Value.VB_UserMemId = 0", "    Value = 42", "End Function"));

    private static async Task<AnalyzerFinding[]> FindingsAsync(string statement)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nPrivate Sub ByLng(ByVal x As Long)\r\nEnd Sub\r\nPrivate Sub ByVar(ByVal x As Variant)\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\nOn Error Resume Next\r\nDim o As Plain, w As Widget, c As Variant, l As Long\r\n"
            + $"{statement.Replace(" ;; ", "\r\n")}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([Plain, Widget], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new ValueOfObjectWithNoDefaultMemberAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("Set o = New Plain ;; c = o", DisplayName = "F16c Let to a Variant")]
    [DataRow("c = CInt(o)", DisplayName = "F16b a conversion")]
    [DataRow("Set o = New Plain ;; c = Split(\"a,b,c\", \",\", o)", DisplayName = "F16a an argument of the library")]
    [DataRow("l = New Plain", DisplayName = "Let to a Long")]
    [DataRow("l = 1 + o", DisplayName = "an operand")]
    [DataRow("ByLng o", DisplayName = "an argument of a value type")]
    public async Task AnObjectWithNoDefaultMember_WhereAValueIsExpected_IsReported(string statement)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(statement));

        Assert.AreEqual(RDCoreDiagnosticId.ValueOfObjectWithNoDefaultMember, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, "'Plain'");
    }

    [TestMethod]
    [DataRow("Set c = New Plain", DisplayName = "a Set")]
    [DataRow("ByVar o", DisplayName = "an argument of a Variant parameter")]
    [DataRow("c = (o Is Nothing)", DisplayName = "an operand of Is")]
    [DataRow("c = Abs(o)", DisplayName = "Abs, which takes a value of any type")]
    [DataRow("l = New Widget", DisplayName = "a class that has a default member")]
    public async Task AnObjectThatIsNotReadForAValue_IsNotReported(string statement)
    {
        Assert.IsEmpty(await FindingsAsync(statement));
    }
}
