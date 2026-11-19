using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The code of a predeclared class that names its own default instance (<c>RDC00410</c>), where the object running the code is <c>Me</c>.
/// </summary>
[TestClass]
public sealed class SuspiciousPredeclaredInstanceAccessAnalyzerTests
{
    // the findings in the class module Factory, as the host models it in a workspace with a Program module that uses it.
    private static async Task<AnalyzerFinding[]> FactoryAsync(params string[] body)
    {
        var source = ModuleWorkspace.ClassModule("Factory", body);
        var payload = await ModuleWorkspace.SemanticsAsync([("Factory", source)], ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "End Sub"));
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Factory");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Factory.cls"));
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. new SuspiciousPredeclaredInstanceAccessAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, model))];
    }

    [TestMethod]
    public async Task APredeclaredClassNamingItsDefaultInstance_IsFound_AndNotMeOrTheClassAsAType()
    {
        var findings = await FactoryAsync(
            "Attribute VB_PredeclaredId = True",
            "Option Explicit",
            "Public Function Create() As Factory",
            "    Set Create = New Factory",
            "    Factory.Init",
            "    Me.Init",
            "    Init",
            "End Function",
            "Public Sub Init()",
            "End Sub");

        CollectionAssert.AreEqual(new[] { 9 }, findings.Select(finding => finding.Range.Start.Line).ToArray(), "a class module's source has five lines of header");
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.SuspiciousPredeclaredInstanceAccess && finding.Severity == DiagnosticSeverity.Warning));
        StringAssert.Contains(findings[0].Message, "'Factory'");
    }

    [TestMethod]
    public async Task AClassThatIsNotPredeclared_HasNoDefaultInstanceToName()
        => Assert.IsEmpty(await FactoryAsync(
            "Option Explicit",
            "Public Function Create() As Factory",
            "    Set Create = New Factory",
            "End Function"));
}
