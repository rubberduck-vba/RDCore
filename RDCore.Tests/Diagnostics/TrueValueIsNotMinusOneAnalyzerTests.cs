using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11031</c>: a condition reads a constant that is <c>True</c> with a value other than <c>-1</c> through an operator that works on its bits
/// (<strong>MS-VBAL §5.6.9.8</strong>): <c>Not 1</c> is <c>-2</c>, which is <c>True</c>; <c>1 = True</c> is <c>False</c>; <c>1 And 2</c> is <c>0</c>.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.9.8 Logical Operators")]
public sealed class TrueValueIsNotMinusOneAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> InCodeAsync(string declarations, string body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\n{declarations}\r\nPublic Sub Probe()\r\n{body}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new TrueValueIsNotMinusOneAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    // a directive is read off the module as it is written: no host is asked.
    private static AnalyzerFinding[] InDirectives(string lines)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\n{lines.Replace(" ;; ", "\r\n")}\r\nPublic Sub Probe()\r\nEnd Sub\r\n";
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new TrueValueIsNotMinusOneAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), null))];
    }

    [TestMethod]
    [DataRow("Private Const DEV = 1", "If Not DEV Then Debug.Print 1", "Not", DisplayName = "Not of a module Const")]
    [DataRow("Private Const DEV = 1", "If DEV = True Then Debug.Print 1", "=", DisplayName = "a module Const compared with True")]
    [DataRow("Private Const DEV = 1", "If DEV <> True Then Debug.Print 1", "<>", DisplayName = "a module Const not equal to True")]
    [DataRow("Private Const DEV = 1", "If DEV Xor True Then Debug.Print 1", "Xor", DisplayName = "Xor with True")]
    [DataRow("Private Const DEV = 1", "If DEV Eqv False Then Debug.Print 1", "Eqv", DisplayName = "Eqv with False")]
    [DataRow("Private Const DEV = 1", "If DEV Imp False Then Debug.Print 1", "Imp", DisplayName = "the left of Imp")]
    [DataRow("Private Const DEV = 1\r\nPrivate Const LOG = 2", "If DEV And LOG Then Debug.Print 1", "And", DisplayName = "And of two constants with no bit in common")]
    [DataRow("Private Const DEV As Long = 2", "If Not DEV Then Debug.Print 1", "Not", DisplayName = "a Const As Long")]
    [DataRow("Private Const ONE = 1\r\nPrivate Const DEV = ONE", "If Not DEV Then Debug.Print 1", "Not", DisplayName = "a Const valued with another")]
    [DataRow("", "Const DEV = 1\r\nIf Not DEV Then Debug.Print 1", "Not", DisplayName = "a procedure's Const")]
    [DataRow("", "If Not 1 Then Debug.Print 1", "Not", DisplayName = "a literal")]
    [DataRow("Private Const DEV = 1", "Dim n As Long\r\nIf n > 0 And Not DEV Then Debug.Print 1", "Not", DisplayName = "through And with a comparison")]
    [DataRow("Private Const DEV = 1", "If (Not DEV) Then Debug.Print 1", "Not", DisplayName = "in parentheses")]
    [DataRow("Private Const DEV = 1", "Do While Not DEV\r\nLoop", "Not", DisplayName = "a Do While condition")]
    [DataRow("Private Const DEV = 1", "Debug.Assert Not DEV", "Not", DisplayName = "a Debug.Assert condition")]
    [DataRow("Private Const DEV = 1", "If False Then\r\nElseIf Not DEV Then\r\nEnd If", "Not", DisplayName = "an ElseIf condition")]
    public async Task ATrueOtherThanMinusOne_ReadThroughItsBits_IsAWarning(string declarations, string body, string op)
    {
        var finding = Assert.ContainsSingle(await InCodeAsync(declarations, body));

        Assert.AreEqual(RDCoreDiagnosticId.TrueValueIsNotMinusOne, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, $"'{op}'");
    }

    [TestMethod]
    [DataRow("Private Const DEV = 1", "If DEV Then Debug.Print 1", DisplayName = "a plain truth test")]
    [DataRow("Private Const DEV = -1", "If Not DEV Then Debug.Print 1", DisplayName = "a Const of -1")]
    [DataRow("Private Const DEV = True", "If Not DEV Then Debug.Print 1", DisplayName = "a Const of True")]
    [DataRow("Private Const DEV = 0", "If Not DEV Then Debug.Print 1", DisplayName = "a Const of 0")]
    [DataRow("Private Const DEV As Boolean = 1", "If Not DEV Then Debug.Print 1", DisplayName = "a Boolean Const, which 1 makes True")]
    [DataRow("Private Const DEV = 1", "If DEV Or False Then Debug.Print 1", DisplayName = "Or")]
    [DataRow("Private Const DEV = 1", "If DEV And True Then Debug.Print 1", DisplayName = "And with True")]
    [DataRow("Private Const DEV = 1\r\nPrivate Const LOG = 3", "If DEV And LOG Then Debug.Print 1", DisplayName = "And of two constants with a bit in common")]
    [DataRow("Private Const DEV = 1", "If DEV = False Then Debug.Print 1", DisplayName = "compared with False")]
    [DataRow("Private Const DEV = 1", "If DEV = 1 Then Debug.Print 1", DisplayName = "compared with a number")]
    [DataRow("Private Const DEV = 1", "If True Imp DEV Then Debug.Print 1", DisplayName = "the right of Imp")]
    [DataRow("Private Const MASK = 4", "Dim attr As Long\r\nIf attr And Not MASK Then Debug.Print 1", DisplayName = "a mask")]
    [DataRow("Private Const DEV = 1", "Dim b As Integer\r\nb = Not DEV", DisplayName = "not a condition")]
    [DataRow("", "Dim b As Boolean\r\nIf Not b Then Debug.Print 1", DisplayName = "a Boolean variable")]
    public async Task ATrueOfMinusOne_OrATruthTheBitsDoNotChange_IsNotReported(string declarations, string body)
        => Assert.IsEmpty(await InCodeAsync(declarations, body));

    [TestMethod]
    [DataRow("#Const DEV = 1 ;; #If Not DEV Then ;; #End If", "Not", DisplayName = "#If Not of a #Const")]
    [DataRow("#Const DEV = 1 ;; #If DEV = True Then ;; #End If", "=", DisplayName = "#If compared with True")]
    [DataRow("#Const DEV = 1 ;; #If False Then ;; #ElseIf Not DEV Then ;; #End If", "Not", DisplayName = "#ElseIf")]
    [DataRow("#Const DEV = 1 ;; #If DEV Xor Win64 Then ;; #End If", "Xor", DisplayName = "Xor with a constant the module does not define")]
    [DataRow("#Const DEV = 1 ;; #Const LOG = 2 ;; #If DEV And LOG Then ;; #End If", "And", DisplayName = "#If And of two #Consts with no bit in common")]
    [DataRow("#Const DEV = 1 ;; #Const RELEASE = Not DEV", "Not", DisplayName = "the value of a #Const")]
    [DataRow("#If Not 1 Then ;; #End If", "Not", DisplayName = "#If Not of a literal")]
    [DataRow("#Const ONE = 1 ;; #Const DEV = ONE ;; #If Not DEV Then ;; #End If", "Not", DisplayName = "a #Const valued with another")]
    [DataRow("#If Not DEV Then ;; #End If ;; #Const DEV = 1", "Not", DisplayName = "a #Const written after the #If")]
    public void ATrueOtherThanMinusOne_InADirective_IsAWarning(string lines, string op)
    {
        var finding = Assert.ContainsSingle(InDirectives(lines));

        Assert.AreEqual(RDCoreDiagnosticId.TrueValueIsNotMinusOne, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, $"'{op}'");
    }

    [TestMethod]
    [DataRow("#Const DEV = 1 ;; #If DEV Then ;; #End If", DisplayName = "#If of a #Const")]
    [DataRow("#Const DEV = -1 ;; #If Not DEV Then ;; #End If", DisplayName = "#Const of -1")]
    [DataRow("#Const DEV = True ;; #If Not DEV Then ;; #End If", DisplayName = "#Const of True")]
    [DataRow("#Const DEV = 1 ;; #If DEV And Win64 Then ;; #End If", DisplayName = "And with a constant the module does not define")]
    [DataRow("#Const DEV = 1 ;; #If DEV Or Mac Then ;; #End If", DisplayName = "Or")]
    [DataRow("#If Not Win64 Then ;; #End If", DisplayName = "Not of a constant the module does not define")]
    [DataRow("#Const DEV = 1 ;; #If DEV = 1 Then ;; #End If", DisplayName = "compared with a number")]
    public void ATruthTheBitsDoNotChange_InADirective_IsNotReported(string lines)
        => Assert.IsEmpty(InDirectives(lines));
}
