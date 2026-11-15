using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Model;
using RDCore.Parsing;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11028</c>: <c>New</c> of a class whose objects are never created, and a variable declared <c>As New</c> of one, which MS-VBA compiles and which
/// raises -2147221164 when it runs (Excel-measured, <c>oracle\s62</c> X04, X06-X09): a Warning, wherever it is written.
/// </summary>
[TestClass]
public sealed class NewOfAClassNotCreatableAnalyzerTests
{
    private static async Task<(AnalyzerFinding[] Findings, string[] Lines)> FindingsAsync(string declarations, string body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations}\r\nPublic Sub Main()\r\n{body}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return ([.. new NewOfAClassNotCreatableAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))
            .OrderBy(finding => finding.Range.Start)], program.Split("\r\n"));
    }

    [TestMethod]
    [DataRow("", "    Dim e As ErrObject\r\n    Set e = New ErrObject", "    Set e = New ErrObject", DisplayName = "s62 X04 New ErrObject")]
    [DataRow("", "    Dim x As Object\r\n    Set x = New VBA.ErrObject", "    Set x = New VBA.ErrObject", DisplayName = "s62 X08 New VBA.ErrObject, Set to an Object")]
    public async Task NewOfErrObject_IsAWarning(string declarations, string body, string line)
    {
        var (findings, lines) = await FindingsAsync(declarations, body);

        var finding = Assert.ContainsSingle(findings);
        Assert.AreEqual(RDCoreDiagnosticId.NewOfAClassNotCreatable, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(string.Format(RDCoreDiagnosticsResources.NewOfAClassNotCreatable_New_Message, "ErrObject"), finding.Message);
        Assert.AreEqual(line, lines[finding.Range.Start.Line]);
        Assert.AreEqual(line.IndexOf("New", StringComparison.Ordinal), finding.Range.Start.Character, "where New is written");
    }

    [TestMethod]
    // a library may name a class with letters that are not ASCII, which its address escapes: the message names the class as it is written. No library of the
    // platform has such a class whose objects are never created, so the New ErrObject of X04 is bound to one.
    public async Task NewOfAClassNamedWithLettersThatAreNotAscii_IsNamedAsItIsWritten()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    Dim e As ErrObject\r\n    Set e = New ErrObject\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var errObject = Assert.ContainsSingle(model.Classes ?? []).Class.AbsoluteUri;
        var binding = SymbolUri.Member(SymbolUri.Module(new Uri("file:///libraries/"), "Bibliothèque"), "Modèle");
        model = model with
        {
            Procedures = [.. model.Procedures.Select(procedure => procedure with
            {
                Expressions = [.. procedure.Expressions.Select(fact => fact.Binding?.AbsoluteUri == errObject ? fact with { Binding = binding } : fact)],
            })],
            Classes = [new ClassFact(binding, "Modèle", IsCreatable: true, IsRegistered: false)],
        };
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));

        var finding = Assert.ContainsSingle(new NewOfAClassNotCreatableAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model)));

        StringAssert.Contains(binding.Fragment, "Mod%C3%A8le", "the address escapes the name");
        Assert.AreEqual(string.Format(RDCoreDiagnosticsResources.NewOfAClassNotCreatable_New_Message, "Modèle"), finding.Message);
    }

    [TestMethod]
    [DataRow("", "    Dim x As New ErrObject\r\n    Debug.Print x.Number", "    Dim x As New ErrObject", DisplayName = "s62 X09 a local")]
    [DataRow("Private x As New ErrObject", "    Debug.Print x.Number", "Private x As New ErrObject", DisplayName = "a variable of the module")]
    [DataRow("", "    Dim a(1) As New ErrObject", "    Dim a(1) As New ErrObject", DisplayName = "an array of them")]
    public async Task AVariableDeclaredAsNewErrObject_IsAWarning(string declarations, string body, string line)
    {
        var (findings, lines) = await FindingsAsync(declarations, body);

        var finding = Assert.ContainsSingle(findings);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, "-2147221164");
        Assert.AreEqual(line, lines[finding.Range.Start.Line]);
    }

    [TestMethod]
    [DataRow("    Dim c As New Collection\r\n    Dim d As Collection\r\n    Set d = New Collection", DisplayName = "s62 X11 a Collection is created")]
    [DataRow("    Dim e As ErrObject\r\n    Set e = Err", DisplayName = "the one ErrObject, Err")]
    public async Task WhatIsCreated_IsNotReported(string body)
        => Assert.IsEmpty((await FindingsAsync("", body)).Findings);
}
