using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11018</c>: integer arithmetic of <c>Abs</c>, <c>Int</c> or <c>Fix</c> of a typed number, which MS-VBA types as the number's type where MS-VBAL declares
/// <c>Variant</c>: it can overflow where a <c>Variant</c> operation would widen (Excel-measured, oracle\s46 F10; HexIDE findings desk #10).
/// </summary>
[TestClass]
public sealed class ArgumentTypedArithmeticAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindingsAsync(string statement)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDim n As Integer, b As Byte, l As Long, d As Double, v As Variant, x As Variant\r\n"
            + statement + "\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new ArgumentTypedArithmeticAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("x = Fix(n) * 7000", "Integer", DisplayName = "Fix of an Integer, times an Integer")]
    [DataRow("x = Int(n) + 32000", "Integer", DisplayName = "Int of an Integer, plus an Integer")]
    [DataRow("x = 1 - Abs(n)", "Integer", DisplayName = "Abs of an Integer, as the right operand")]
    [DataRow("x = Abs(b) + b", "Byte", DisplayName = "Abs of a Byte, plus a Byte")]
    [DataRow("x = Fix(l) * l", "Long", DisplayName = "Fix of a Long, times a Long")]
    [DataRow("x = VBA.Fix(n) * 7000", "Integer", DisplayName = "qualified by the library")]
    public async Task IntegerArithmeticOfACallTypedByItsArgument_IsAWarning(string statement, string type)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(statement));

        Assert.AreEqual(RDCoreDiagnosticId.ArgumentTypedArithmeticOverflow, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, type);
    }

    [TestMethod]
    [DataRow("x = Fix(v) * 7000", DisplayName = "a Variant argument: the call is a Variant, as declared")]
    [DataRow("x = Fix(d) * 7000", DisplayName = "a Double: Double arithmetic overflows as a Variant's does")]
    [DataRow("x = Fix(n) / 7", DisplayName = "a division is a Double")]
    [DataRow("x = Sgn(n) * 7000", DisplayName = "Sgn returns an Integer, as MS-VBAL declares it")]
    [DataRow("x = Asc(\"A\") * 700", DisplayName = "so does Asc")]
    [DataRow("x = Fix(n)", DisplayName = "no operation")]
    public async Task AnythingElse_IsNotReported(string statement)
        => Assert.IsEmpty(await FindingsAsync(statement));
}
