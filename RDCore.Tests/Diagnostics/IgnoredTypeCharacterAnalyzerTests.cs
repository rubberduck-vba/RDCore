using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11019</c>: a reserved function of the library written with a type character that does not match its type, which MS-VBAL §5.6.10 makes invalid and
/// MS-VBA ignores (Excel-measured, oracle\s46 F18, oracle\s44 D01-D18; HexIDE findings desk #18).
/// </summary>
[TestClass]
public sealed class IgnoredTypeCharacterAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindingsAsync(string expression)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDebug.Print {expression}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new IgnoredTypeCharacterAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("Len$(\"abc\")", "Len")]
    [DataRow("CVar$(5)", "CVar")]
    [DataRow("Abs%(-3)", "Abs")]
    [DataRow("CInt$(2.6)", "CInt")]
    public async Task ATypeCharacterThatIsNotTheFunctionsType_IsAWarning(string expression, string name)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(expression));

        Assert.AreEqual(RDCoreDiagnosticId.IgnoredTypeCharacter, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, $"'{name}'");
    }

    [TestMethod]
    [DataRow("CStr$(1)", DisplayName = "the character is the function's type")]
    [DataRow("Left$(\"ab\", 1)", DisplayName = "a function that has a $ form of its own")]
    [DataRow("Len(\"abc\")", DisplayName = "no character")]
    public async Task AnythingElse_IsNotReported(string expression)
        => Assert.IsEmpty(await FindingsAsync(expression));
}
