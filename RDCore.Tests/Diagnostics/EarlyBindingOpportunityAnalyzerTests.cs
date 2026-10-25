using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Libraries;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00311</c>: a <c>CreateObject</c> call that creates an object of a class a referenced library declares, which could be declared as the class and created
/// with <c>New</c>. Which class it is is a fact the host found (<c>ExpressionFact.Creates</c>).
/// </summary>
[TestClass]
public sealed class EarlyBindingOpportunityAnalyzerTests
{
    private static readonly LibraryDescription Lab = new()
    {
        Name = "Lab",
        Classes = [new ClassDescription { Name = "Shelf", ProgId = "Lab.Shelf", IsCreatable = true }],
    };

    private static async Task<AnalyzerFinding[]> RunAsync(string[] references, string body)
    {
        var source = $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDim o As Object\r\n{body}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], source, libraries: new ModuleWorkspace.WorkspaceLibraries(references, new InMemoryLibrarySource(Lab)));
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        var parsed = new ModuleParser().Parse(uri, source);
        return [.. new EarlyBindingOpportunityAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, payload.Modules.Single()))];
    }

    [TestMethod]
    public async Task CreateObject_OfAClassOfAReferencedLibrary_IsASuggestion()
    {
        var findings = await RunAsync(["Lab"], "Set o = CreateObject(\"Lab.Shelf\")");

        Assert.HasCount(1, findings);
        Assert.AreEqual(RDCoreDiagnosticId.EarlyBindingOpportunity, findings[0].Id);
        Assert.AreEqual(DiagnosticSeverity.Information, findings[0].Severity);
        Assert.AreEqual("Set o = ".Length, findings[0].Range.Start.Character, "where the call is written");
        StringAssert.Contains(findings[0].Message, "Lab.Shelf");
    }

    [TestMethod]
    [DataRow(new string[0], "Set o = CreateObject(\"Lab.Shelf\")", DisplayName = "a library the project does not reference")]
    [DataRow(new[] { "Lab" }, "Set o = CreateObject(\"Lab.Shelf\", \"faraway\")", DisplayName = "another machine")]
    [DataRow(new[] { "Lab" }, "Set o = CreateObject(\"No.Such\")", DisplayName = "a class nothing describes")]
    [DataRow(new[] { "Lab" }, "Set o = New Lab.Shelf", DisplayName = "early-bound already")]
    public async Task AnObjectThatCannotBeNewedHere_OrIsAlready_IsNoSuggestion(string[] references, string body)
        => Assert.IsEmpty(await RunAsync(references, body));
}
