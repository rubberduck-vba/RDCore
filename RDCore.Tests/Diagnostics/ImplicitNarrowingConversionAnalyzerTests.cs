using System.Globalization;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Model;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00112</c>: the host evaluates the code of each procedure when it loads it, and the analyzer says what is worth saying about the conversions the code
/// asks for without saying so.
/// </summary>
[TestClass]
public sealed class ImplicitNarrowingConversionAnalyzerTests
{
    private const string Source = """
        Attribute VB_Name = "Program"
        Option Explicit
        Public Sub Work(ByVal d As Double, ByVal l As Long, ByVal v As Variant)
            Dim n As Long
            Dim i As Integer
            Dim s As Single
            n = d
            d = l
            n = CLng(d)
            i = l
            s = d
            n = l
        End Sub
        Public Function Total(ByVal d As Double) As Long
            Total = d
        End Function
        """;

    private static async Task<AnalyzerFinding[]> FindAsync(string source)
    {
        var uri = TestUri.TestModuleUri();
        var payload = await ModuleWorkspace.SemanticsAsync([], source.ReplaceLineEndings("\r\n") + "\r\n");

        return [.. new ImplicitNarrowingConversionAnalyzer()
            .Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), payload.Modules.Single()))
            .OrderBy(finding => finding.Range.Start)];
    }

    private static int[] Lines(AnalyzerFinding[] findings) => [.. findings.Select(finding => finding.Range.Start.Line)];

    [TestMethod]
    public async Task AValueConvertedIntoATypeThatHoldsLessOfIt_WithoutAConversionFunction_IsAWarning()
    {
        var findings = await FindAsync(Source);

        // n = d (Double to Long), i = l (Long to Integer), s = d (Double to Single), Total = d (Double to Long).
        CollectionAssert.AreEqual(new[] { 6, 9, 10, 14 }, Lines(findings));
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.ImplicitNarrowingConversion));
        Assert.IsTrue(findings.All(finding => finding.Severity == DiagnosticSeverity.Warning));
    }

    [TestMethod]
    public async Task TheMessage_NamesBothTypes_AndSaysWhenTheFractionIsRounded()
    {
        var findings = await FindAsync(Source);

        Assert.AreEqual(
            string.Format(CultureInfo.CurrentUICulture, RDCoreDiagnosticsResources.ImplicitNarrowingConversion_Lossy_Message, "Double", "Long"),
            findings[0].Message, "Double to Long loses the fraction");
        Assert.AreEqual(
            string.Format(CultureInfo.CurrentUICulture, RDCoreDiagnosticsResources.ImplicitNarrowingConversion_Message, "Long", "Integer"),
            findings[1].Message, "Long to Integer loses range, not a fraction");
    }

    [TestMethod]
    public async Task AConversionTheCodeWritesOut_AWideningOne_AndOneIntoTheSameType_AreNotReported()
    {
        var findings = await FindAsync(Source);

        CollectionAssert.DoesNotContain(Lines(findings), 7, "d = l widens");
        CollectionAssert.DoesNotContain(Lines(findings), 8, "CLng says so");
        CollectionAssert.DoesNotContain(Lines(findings), 11, "n = l is the same type");
    }

    [TestMethod]
    public async Task AKnownValueThatFitsAndLosesNothing_NeedsNoConversionFunction_ButOneThatLosesItsFraction_Does()
    {
        var findings = await FindAsync("""
            Attribute VB_Name = "Program"
            Option Explicit
            Private Const Limit As Long = 10
            Public Sub Work()
                Dim b As Byte
                Dim n As Long
                b = 10
                b = Limit
                n = 2.5
                b = 300
            End Sub
            """);

        // b = 10 and b = Limit fit; n = 2.5 loses its fraction; b = 300 cannot fit, which is a run-time error the language reports (VBR00006), not a warning.
        CollectionAssert.AreEqual(new[] { 8 }, Lines(findings));
    }

    [TestMethod]
    public async Task AnArgumentPassedToAParameterOfANarrowerType_AndASumAssignedToAByte_AreReported()
    {
        var findings = await FindAsync("""
            Attribute VB_Name = "Program"
            Option Explicit
            Private Sub Show(ByVal n As Integer)
            End Sub
            Public Sub Work(ByVal total As Long)
                Dim result As Byte
                Show total
                result = result + 1
            End Sub
            """);

        CollectionAssert.AreEqual(new[] { 6, 7 }, Lines(findings));
    }

    [TestMethod]
    public async Task WhatTheStandardLibraryReturnsForAnArgumentThatIsNotKnown_IsReportedLikeAVariableThatIsNotKnown()
    {
        var findings = await FindAsync("""
            Attribute VB_Name = "Program"
            Option Explicit
            Public Sub Work(ByVal v As Variant, ByVal n As Long)
                Dim b As Byte
                b = 200 + n
                b = 200 + CLng(v)
                b = 200 + Len("abc")
            End Sub
            """);

        // 200 + n and 200 + CLng(v) are Longs that are not known to fit; 200 + Len("abc") is 203, which does.
        CollectionAssert.AreEqual(new[] { 4, 5 }, Lines(findings));
    }

    [TestMethod]
    public void AProcedureThatWasNotEvaluated_HasNothingToReport()
    {
        var uri = TestUri.TestModuleUri();
        var module = new RDCore.SDK.Platform.Protocol.ModuleSemanticsDto(uri, true, [], [new RDCore.SDK.Platform.Protocol.ProcedureSemanticsDto(uri, true, [], [])], []);

        var findings = new ImplicitNarrowingConversionAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, "Attribute VB_Name = \"Program\"\r\n"), module));

        Assert.IsEmpty(findings.ToArray());
    }
}
