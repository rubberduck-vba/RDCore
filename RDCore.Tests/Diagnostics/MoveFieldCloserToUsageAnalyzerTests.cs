using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00413</c>: a <c>Private</c> variable of the module whose every reference is in one procedure.
/// </summary>
[TestClass]
public sealed class MoveFieldCloserToUsageAnalyzerTests
{
    private static readonly string Source = ModuleWorkspace.ClassModule("Source", "Public Event Changed()", "Public Sub Raise()", "RaiseEvent Changed", "End Sub");

    // the names of the variables found in the Counter class, and the procedures named in the messages.
    private static async Task<(string Name, string Procedure)[]> FoundAsync(params string[] body)
    {
        var counter = ModuleWorkspace.ClassModule("Counter", body);
        var program = ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "Dim c As New Counter", "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([("Source", Source), ("Counter", counter)], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Counter");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Counter.cls"));

        var findings = new MoveFieldCloserToUsageAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, counter), model)).ToArray();
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.MoveFieldCloserToUsage && finding.Severity == DiagnosticSeverity.Information));
        return [.. findings.Select(finding => finding.Message.Split('\'')).Select(parts => (parts[1], parts[3]))];
    }

    [TestMethod]
    public async Task AVariableOneProcedureUses_IsFound()
        => CollectionAssert.AreEqual(
            new[] { ("mTotal", "Sum") },
            await FoundAsync("Private mTotal As Long", "Private mShared As Long",
                "Public Function Sum() As Long", "mTotal = 0", "mTotal = mTotal + mShared", "Sum = mTotal", "End Function",
                "Public Sub Bump()", "mShared = mShared + 1", "End Sub"));

    [TestMethod]
    public async Task ABackingVariableOfAProperty_UsedByItsGetAndItsLet_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("Private mValue As Long",
            "Public Property Get Value() As Long", "Value = mValue", "End Property",
            "Public Property Let Value(ByVal v As Long)", "mValue = v", "End Property"));

    [TestMethod]
    public async Task AWithEventsVariable_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("Private WithEvents mSource As Source",
            "Private Sub Class_Initialize()", "Set mSource = New Source", "End Sub",
            "Private Sub mSource_Changed()", "End Sub"));

    [TestMethod]
    public async Task AVariableNoCodeUses_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("Private mUnused As Long", "Public Sub Work()", "End Sub"));
}
