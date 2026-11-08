using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11005</c>, <c>RDC11006</c> and <c>RDC11011</c>: analyzers that read what the host states about the operations of the code (<see cref="RDCore.SDK.Platform.Protocol.OperatorFactDto"/>).
/// </summary>
[TestClass]
public sealed class OperatorFactAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindAsync(IModuleAnalyzer analyzer, string parameters, params string[] body)
    {
        var uri = TestUri.TestModuleUri();
        var source = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main({parameters})\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], source);

        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), payload.Modules.Single()))];
    }

    [TestMethod]
    public async Task TheAdditionOfTwoStrings_IsAConcatenation_ThatTheConcatenationOperatorSays()
    {
        var finding = (await FindAsync(new AmbiguousConcatenationAnalyzer(), "ByVal s As String, ByVal t As String", "Dim r As String", "r = s + t")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.AmbiguousConcatenation, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Information, finding.Severity);
        Assert.AreEqual(4, finding.Range.Start.Line);
    }

    [TestMethod]
    [DataRow("ByVal a As Long, ByVal b As Long", DisplayName = "two numbers")]
    [DataRow("ByVal a As String, ByVal b As Long", DisplayName = "a String and a number")]
    public async Task AnAdditionThatAdds_IsNotAConcatenation(string parameters)
    {
        Assert.IsEmpty(await FindAsync(new AmbiguousConcatenationAnalyzer(), parameters, "Dim r As Variant", "r = a + b"));
    }

    [TestMethod]
    public async Task TheConcatenationOperator_IsNotReported()
    {
        Assert.IsEmpty(await FindAsync(new AmbiguousConcatenationAnalyzer(), "ByVal s As String, ByVal t As String", "Dim r As String", "r = s & t"));
    }

    [TestMethod]
    public async Task ALogicalOperatorOverNumbers_WorksOnTheirBits()
    {
        var finding = (await FindAsync(new BitwiseSemanticsAnalyzer(), "ByVal a As Long, ByVal b As Long", "Dim r As Long", "r = a And b")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.BitwiseOperator, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Hint, finding.Severity);
    }

    [TestMethod]
    public async Task ALogicalOperatorOverBooleans_IsLogic()
    {
        Assert.IsEmpty(await FindAsync(new BitwiseSemanticsAnalyzer(), "ByVal a As Boolean, ByVal b As Boolean", "Dim r As Boolean", "r = a And b"));
    }

    // what two Variants hold is not known: neither is whether the operator works on their bits.
    [TestMethod]
    public async Task ALogicalOperatorOverVariantsThatAreNotKnown_IsNotReported()
    {
        Assert.IsEmpty(await FindAsync(new BitwiseSemanticsAnalyzer(), "ByVal a As Variant, ByVal b As Variant", "Dim r As Variant", "r = a And b"));
    }

    [TestMethod]
    [DataRow("<")]
    [DataRow("=")]
    [DataRow(">=")]
    public async Task AVariantNumberComparedWithAVariantString_IsLessWhateverTheirValues(string op)
    {
        var finding = (await FindAsync(new VariantNumberStringComparisonAnalyzer(), "",
            "Dim a As Variant, b As Variant, r As Boolean", "a = 5", "b = \"4\"", $"r = a {op} b")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.VariantNumberStringComparison, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(6, finding.Range.Start.Line);
    }

    [TestMethod]
    public async Task AVariantNumberComparedWithAVariantString_FromAnInputBox_IsReported()
    {
        var finding = (await FindAsync(new VariantNumberStringComparisonAnalyzer(), "",
            "Dim n As Variant, i As Variant", "n = InputBox(\"How many?\")", "i = 1", "If i <= n Then Debug.Print i")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.VariantNumberStringComparison, finding.Id);
    }

    [TestMethod]
    [DataRow("Dim a As Long, b As Variant, r As Boolean", DisplayName = "a declared number")]
    [DataRow("Dim a As Variant, b As String, r As Boolean", DisplayName = "a declared String")]
    public async Task AComparisonWithADeclaredOperand_ComparesTheValues(string declarations)
    {
        Assert.IsEmpty(await FindAsync(new VariantNumberStringComparisonAnalyzer(), "", declarations, "a = 5", "b = \"4\"", "r = a < b"));
    }

    [TestMethod]
    public async Task TwoVariantNumbers_AreComparedAsNumbers()
    {
        Assert.IsEmpty(await FindAsync(new VariantNumberStringComparisonAnalyzer(), "",
            "Dim a As Variant, b As Variant, r As Boolean", "a = 5", "b = 4", "r = a < b"));
    }

    // RDC11013: MS-VBA compares a Variant number with a String as text, where MS-VBAL's table makes the String that is no number a type mismatch.
    [TestMethod]
    [DataRow("a = 10", "b = \"9\"", DisplayName = "a number")]
    [DataRow("a = #1/2/2020#", "b = \"x\"", DisplayName = "a Date")]
    [DataRow("a = True", "b = \"x\"", DisplayName = "a Boolean")]
    public async Task AVariantNumberComparedWithAString_IsComparedAsText(string first, string second)
    {
        var finding = (await FindAsync(new VariantComparedWithStringAsTextAnalyzer(), "",
            "Dim a As Variant, b As String, r As Boolean", first, second, "r = a < b")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.VariantComparedWithStringAsText, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(6, finding.Range.Start.Line);
    }

    [TestMethod]
    [DataRow("Dim a As Variant, b As Variant, r As Boolean", "a = 5", DisplayName = "two Variants")]
    [DataRow("Dim a As Long, b As String, r As Boolean", "a = 5", DisplayName = "two declared types")]
    [DataRow("Dim a As Variant, b As String, r As Boolean", "a = \"5\"", DisplayName = "a Variant String")]
    public async Task AComparisonOfAnotherKind_IsNotComparedAsText(string declarations, string first)
    {
        Assert.IsEmpty(await FindAsync(new VariantComparedWithStringAsTextAnalyzer(), "", declarations, first, "b = \"4\"", "r = a < b"));
    }

    [TestMethod]
    [DataRow("a[]", DisplayName = "[]")]
    [DataRow("[!]", DisplayName = "[!]")]
    [DataRow("a*[z-a]", DisplayName = "a reversed range")]
    [DataRow("[az-a]", DisplayName = "a reversed range after a match")]
    [DataRow("x[", DisplayName = "an unterminated list")]
    [TestCategory("MS-VBAL 5.6.9.6 Binary 'Like' Operator")]
    public async Task ALikePatternMsVbaMatchesOtherwiseThanMsVbal_IsReported(string pattern)
    {
        var finding = (await FindAsync(new LikePatternDepartureAnalyzer(), "", "Dim r As Variant", $"r = \"a\" Like \"{pattern}\"")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.LikePatternDeparture, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(4, finding.Range.Start.Line);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.6 Binary 'Like' Operator")]
    public async Task ALikePatternWithBothDepartures_IsReportedForEach()
        => Assert.HasCount(2, await FindAsync(new LikePatternDepartureAnalyzer(), "", "Dim r As Variant", "r = \"a\" Like \"[]x[\""));

    [TestMethod]
    [DataRow("a*[a-z]", DisplayName = "a valid pattern")]
    [DataRow("[-a]", DisplayName = "a - at the start")]
    [TestCategory("MS-VBAL 5.6.9.6 Binary 'Like' Operator")]
    public async Task AValidLikePattern_IsNotReported(string pattern)
        => Assert.IsEmpty(await FindAsync(new LikePatternDepartureAnalyzer(), "", "Dim r As Variant", $"r = \"a\" Like \"{pattern}\""));

    // only a pattern written in the code is the same wherever the operation runs.
    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.6 Binary 'Like' Operator")]
    public async Task ALikePatternThatIsNotLiteral_IsNotReported()
        => Assert.IsEmpty(await FindAsync(new LikePatternDepartureAnalyzer(), "ByVal p As String", "Dim r As Variant", "p = \"a[]\"", "r = \"a\" Like p"));

    // a range is folded before it is tested in text mode: [Z-a] is [z-a] there, and [a-Z] is [a-z].
    [TestMethod]
    [DataRow("[Z-a]", "Binary", 0)]
    [DataRow("[Z-a]", "Text", 1)]
    [DataRow("[a-Z]", "Binary", 1)]
    [DataRow("[a-Z]", "Text", 0)]
    [TestCategory("MS-VBAL 5.6.9.6 Binary 'Like' Operator")]
    public async Task AReversedRange_IsTheOneOfTheComparisonMode(string pattern, string compare, int expected)
    {
        var uri = TestUri.TestModuleUri();
        var source = $"Attribute VB_Name = \"Program\"\r\nOption Compare {compare}\r\nPublic Sub Main()\r\nDim r As Variant\r\n"
            + $"r = \"a\" Like \"{pattern}\"\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], source);

        Assert.HasCount(expected,
            new LikePatternDepartureAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), payload.Modules.Single())));
    }

    [TestMethod]
    [DataRow("Dim a As Variant, b As Long, r As Boolean", "a = \"abc\"", DisplayName = "a Long")]
    [DataRow("Dim a As Variant, b As Date, r As Boolean", "a = \"abc\"", DisplayName = "a Date")]
    [DataRow("Dim a As Variant, b As Boolean, r As Boolean", "a = \"\"", DisplayName = "a Boolean, and an empty String")]
    public async Task AVariantStringThatIsNoNumber_ComparedWithANumber_IsRanked(string declarations, string first)
    {
        var finding = (await FindAsync(new VariantStringThatIsNoNumberRankedAnalyzer(), "", declarations, first, "b = 5", "r = a < b")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.VariantStringThatIsNoNumberRanked, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(6, finding.Range.Start.Line);
    }

    [TestMethod]
    [DataRow("Dim a As Variant, b As Long, r As Boolean", "a = \"3\"", DisplayName = "a number's text")]
    [DataRow("Dim a As Variant, b As Variant, r As Boolean", "a = \"abc\"", DisplayName = "two Variants (RDC11011)")]
    [DataRow("Dim a As String, b As Long, r As Boolean", "a = \"3\"", DisplayName = "a String declared as one")]
    public async Task AComparisonOfAnotherKind_IsNotRanked(string declarations, string first)
    {
        Assert.IsEmpty(await FindAsync(new VariantStringThatIsNoNumberRankedAnalyzer(), "", declarations, first, "b = 5", "r = a < b"));
    }

    // RDC11027: MS-VBA compares a String with a Byte, an Integer or a Long, and a Variant String with a Single or a LongLong, as Doubles (oracle\s56 G, H).
    [TestMethod]
    [DataRow("Dim a As Variant, b As Long, r As Boolean", "a = \"2.5\"", "b = 2", DisplayName = "a Variant \"2.5\" and a Long (G01)")]
    [DataRow("Dim a As String, b As Integer, r As Boolean", "a = \"2.5\"", "b = 2", DisplayName = "a String \"2.5\" and an Integer (G03)")]
    [DataRow("Dim a As Variant, b As Byte, r As Boolean", "a = \"2.5\"", "b = 2", DisplayName = "a Variant \"2.5\" and a Byte")]
    [DataRow("Dim a As Variant, b As Integer, r As Boolean", "a = \"40000\"", "b = 2", DisplayName = "a number the Integer does not hold (H01)")]
    [DataRow("Dim a As Variant, b As Byte, r As Boolean", "a = \"-1\"", "b = 2", DisplayName = "a number the Byte does not hold")]
    [DataRow("Dim a As Variant, b As Single, r As Boolean", "a = \"0.1\"", "b = 0.1", DisplayName = "a Variant \"0.1\" and a Single (H02)")]
    [DataRow("Dim a As Variant, b As LongLong, r As Boolean", "a = \"2.5\"", "b = 2", DisplayName = "a Variant \"2.5\" and a LongLong (H03)")]
    [DataRow("Dim a As Variant, b As LongLong, r As Boolean", "a = \"9007199254740993\"", "b = 9007199254740992^", DisplayName = "a LongLong no Double holds (H04)")]
    public async Task AStringComparedWithANumber_AsDoubles_IsReported(string declarations, string first, string second)
    {
        var finding = (await FindAsync(new StringComparedWithANumberAsADoubleAnalyzer(), "", declarations, first, second, "r = a = b")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.StringComparedWithANumberAsADouble, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(6, finding.Range.Start.Line);
    }

    // the analysis knows what a Variant holds, and not the value of a variable declared with its type: it is what the code is run with.
    [TestMethod]
    [DataRow("ByVal a As String", "r = a > 2", DisplayName = "a String parameter")]
    [DataRow("", "Dim a As String", "a = \"2\"", "r = a > 2", DisplayName = "a String variable")]
    [DataRow("ByVal b As LongLong", "Dim v As Variant", "v = \"2.5\"", "r = v = b", DisplayName = "a LongLong parameter")]
    public async Task AComparisonOfAValueThatIsNotKnown_AsDoubles_IsReported(string parameters, params string[] body)
    {
        var finding = (await FindAsync(new StringComparedWithANumberAsADoubleAnalyzer(), parameters, ["Dim r As Boolean", .. body])).Single();

        Assert.AreEqual(RDCoreDiagnosticId.StringComparedWithANumberAsADouble, finding.Id);
    }

    [TestMethod]
    [DataRow("r = \"abc\" = b", DisplayName = "a String that is no number, 13 either way")]
    [DataRow("r = \"1e400\" = b", DisplayName = "a String no Double holds, 6 either way")]
    [DataRow("r = \"2\" = b", DisplayName = "an integer's text, which the two compare alike")]
    [DataRow("v = \"2\"", "r = v = 2^", DisplayName = "a LongLong a Double holds")]
    [DataRow("v = \"2\"", "r = v = c", DisplayName = "a LongLong parameter and an integer's text")]
    public async Task AComparisonOfKnownValues_ThatComeOutAlike_IsNotReported(params string[] body)
    {
        Assert.IsEmpty(await FindAsync(new StringComparedWithANumberAsADoubleAnalyzer(), "ByVal b As Long, ByVal c As LongLong", ["Dim r As Boolean, v As Variant", .. body]));
    }

    // "2.5" is the table's 2, and "2.6" its 3: with n not known, MS-VBA's answer is the table's for some operators and not for others.
    [TestMethod]
    [DataRow("2.5", "<", false, DisplayName = "\"2.5\" < n: 2 < n for every integer")]
    [DataRow("2.5", ">=", false, DisplayName = "\"2.5\" >= n: 2 >= n for every integer")]
    [DataRow("2.5", ">", true, DisplayName = "\"2.5\" > n: not 2 > n for n = 2")]
    [DataRow("2.5", "<=", true, DisplayName = "\"2.5\" <= n: not 2 <= n for n = 2")]
    [DataRow("2.5", "=", true, DisplayName = "\"2.5\" = n: not 2 = n for n = 2")]
    [DataRow("2.5", "<>", true, DisplayName = "\"2.5\" <> n: not 2 <> n for n = 2")]
    [DataRow("2.6", ">", false, DisplayName = "\"2.6\" > n: 3 > n for every integer")]
    [DataRow("2.6", "<=", false, DisplayName = "\"2.6\" <= n: 3 <= n for every integer")]
    [DataRow("2.6", "<", true, DisplayName = "\"2.6\" < n: not 3 < n for n = 3")]
    [DataRow("2.6", ">=", true, DisplayName = "\"2.6\" >= n: not 3 >= n for n = 3")]
    [DataRow("-2.5", "<", true, DisplayName = "\"-2.5\" < n: not -2 < n for n = -2")]
    [DataRow("-2.5", ">=", true, DisplayName = "\"-2.5\" >= n: not -2 >= n for n = -2")]
    [DataRow("-2.5", ">", false, DisplayName = "\"-2.5\" > n: -2 > n for every integer")]
    [DataRow("-2.5", "<=", false, DisplayName = "\"-2.5\" <= n: -2 <= n for every integer")]
    [DataRow("3.5", "<", true, DisplayName = "\"3.5\" < n: not 4 < n for n = 4, half to even")]
    [DataRow("3.5", ">=", true, DisplayName = "\"3.5\" >= n: not 4 >= n for n = 4, half to even")]
    [DataRow("3.5", ">", false, DisplayName = "\"3.5\" > n: 4 > n for every integer, half to even")]
    [DataRow("3.5", "<=", false, DisplayName = "\"3.5\" <= n: 4 <= n for every integer, half to even")]
    public async Task AStringComparedWithANumberNotKnown_IsReported_WhereTheOperatorCanAnswerOtherwise(string text, string op, bool isReported)
        => await AssertReportedEitherSide("Long", text, op, isReported);

    [TestMethod]
    [DataRow("Byte", "255.5", "=", true, DisplayName = "a Byte and \"255.5\": the table's 256 overflows (6)")]
    [DataRow("Byte", "255.5", "<", true, DisplayName = "a Byte and \"255.5\" <: the table's 256 overflows (6)")]
    [DataRow("Integer", "-32768.5", "<", true, DisplayName = "an Integer and \"-32768.5\" <: not -32768 < n for n = -32768")]
    [DataRow("Integer", "-32768.5", ">=", true, DisplayName = "an Integer and \"-32768.5\" >=: not -32768 >= n for n = -32768")]
    [DataRow("Integer", "-32768.5", ">", false, DisplayName = "an Integer and \"-32768.5\" >: -32768 > n for every Integer")]
    [DataRow("Integer", "-32768.5", "<=", false, DisplayName = "an Integer and \"-32768.5\" <=: -32768 <= n for every Integer")]
    [DataRow("Single", "16777217", "=", true, DisplayName = "a Single and \"16777217\": the table's Single is 16777216")]
    [DataRow("Single", "16777216", "=", false, DisplayName = "a Single and \"16777216\", which the Single holds")]
    [DataRow("LongLong", "9007199254740993", "=", true, DisplayName = "a LongLong and \"9007199254740993\", past 2^53")]
    [DataRow("LongLong", "9007199254740993", "<", true, DisplayName = "a LongLong and \"9007199254740993\" <, past 2^53")]
    public async Task AStringComparedWithANumberNotKnownOfAType_IsReported_WhereTheOperatorCanAnswerOtherwise(string type, string text, string op, bool isReported)
        => await AssertReportedEitherSide(type, text, op, isReported);

    private static async Task AssertReportedEitherSide(string type, string text, string op, bool isReported)
    {
        string[] stringLeft = ["Dim v As Variant, r As Boolean", $"v = \"{text}\"", $"r = v {op} n"];
        string[] stringRight = ["Dim v As Variant, r As Boolean", $"v = \"{text}\"", $"r = n {Mirrored(op)} v"];

        Assert.AreEqual(isReported, (await FindAsync(new StringComparedWithANumberAsADoubleAnalyzer(), $"ByVal n As {type}", stringLeft)).Length == 1, "the String on the left");
        Assert.AreEqual(isReported, (await FindAsync(new StringComparedWithANumberAsADoubleAnalyzer(), $"ByVal n As {type}", stringRight)).Length == 1, "the String on the right");

        static string Mirrored(string op) => op switch { "<" => ">", ">" => "<", "<=" => ">=", ">=" => "<=", _ => op };
    }

    [TestMethod]
    [DataRow("v = \"2.5\"", "r = v > 7", false, DisplayName = "\"2.5\" > 7: False either way")]
    [DataRow("v = \"2.5\"", "r = v > 2", true, DisplayName = "\"2.5\" > 2: True, where 2 > 2 is False")]
    [DataRow("v = \"2.5\"", "r = v <> 3", false, DisplayName = "\"2.5\" <> 3: True either way")]
    [DataRow("v = \"2.5\"", "r = v <> 2", true, DisplayName = "\"2.5\" <> 2: True, where 2 <> 2 is False")]
    [DataRow("", "r = S > 2", true, DisplayName = "a constant \"2.5\" > 2")]
    [DataRow("", "r = S < 7", false, DisplayName = "a constant \"2.5\" < 7")]
    [DataRow("", "r = S <= K", true, DisplayName = "a constant \"2.5\" <= a constant 2: False, where 2 <= 2 is True")]
    [DataRow("", "r = S >= K", false, DisplayName = "a constant \"2.5\" >= a constant 2: True either way")]
    [DataRow("v = \"9007199254740993\"", "r = v = L", false, DisplayName = "\"9007199254740993\" = a constant LongLong 9007199254740996: False either way")]
    [DataRow("v = \"9007199254740993\"", "r = v = M", true, DisplayName = "\"9007199254740993\" = a constant LongLong 9007199254740992: True, where the table's is False")]
    public async Task AStringComparedWithAKnownNumber_IsReported_WhereTheAnswersDiffer(string assignment, string comparison, bool isReported)
    {
        var findings = await FindAsync(new StringComparedWithANumberAsADoubleAnalyzer(), "",
            "Const S As String = \"2.5\"", "Const K As Long = 2", "Const L As LongLong = 9007199254740996^", "Const M As LongLong = 9007199254740992^",
            "Dim v As Variant, r As Boolean", assignment, comparison);

        Assert.AreEqual(isReported, findings.Length == 1);
    }

    // a Case clause compares the selector with its value as the relational operator does (MS-VBAL 5.4.2.10).
    [TestMethod]
    [DataRow("Case 2", true, DisplayName = "Case 2: v = 2")]
    [DataRow("Case 7", false, DisplayName = "Case 7: v = 7, False either way")]
    [DataRow("Case Is > 2", true, DisplayName = "Case Is > 2")]
    [DataRow("Case Is < 3", false, DisplayName = "Case Is < 3: True either way")]
    [DataRow("Case 1 To 2", true, DisplayName = "Case 1 To 2: v <= 2")]
    [DataRow("Case 3 To 4", false, DisplayName = "Case 3 To 4: False either way")]
    public async Task ACaseClause_OfAVariantSelectorHoldingAString_IsReported_WhereTheAnswersDiffer(string clause, bool isReported)
    {
        var findings = await FindAsync(new StringComparedWithANumberAsADoubleAnalyzer(), "",
            "Dim v As Variant", "v = \"2.5\"", "Select Case v", clause, "Debug.Print 1", "End Select");

        Assert.AreEqual(isReported, findings.Length > 0);
    }

    [TestMethod]
    [DataRow("Dim a As Variant, b As Long, r As Boolean", "a = \"2\"", "b = 2", DisplayName = "an integer's text, which the two compare alike")]
    [DataRow("Dim a As Variant, b As Double, r As Boolean", "a = \"2.5\"", "b = 2", DisplayName = "a Double, the table's type")]
    [DataRow("Dim a As Variant, b As Currency, r As Boolean", "a = \"2.00001\"", "b = 2", DisplayName = "a Currency, the table's type (I02)")]
    [DataRow("Dim a As Variant, b As Boolean, r As Boolean", "a = \"2.5\"", "b = True", DisplayName = "a Boolean, the table's type")]
    [DataRow("Dim a As String, b As Single, r As Boolean", "a = \"0.1\"", "b = 0.1", DisplayName = "a String declared as one and a Single, the table's type (H02)")]
    [DataRow("Dim a As Variant, b As Variant, r As Boolean", "a = \"2.5\"", "b = 2", DisplayName = "two Variants (RDC11011)")]
    [DataRow("Dim a As Variant, b As Long, r As Boolean", "a = \"abc\"", "b = 2", DisplayName = "a Variant String that is no number (RDC11020)")]
    public async Task AComparisonOfAnotherKind_IsNotReportedAsDoubles(string declarations, string first, string second)
    {
        Assert.IsEmpty(await FindAsync(new StringComparedWithANumberAsADoubleAnalyzer(), "", declarations, first, second, "r = a = b"));
    }

    // an expression over a Variant number is a Variant number, which MS-VBA compares with a String as text (RDC11013), not as a Double.
    [TestMethod]
    public async Task AnExpressionOverAVariantNumber_ComparedWithAString_IsComparedAsText_NotAsDoubles()
    {
        string[] body = ["Dim v As Variant, r As Boolean", "v = 1", "r = (v + 1) = \"2.5\""];

        Assert.AreEqual(RDCoreDiagnosticId.VariantComparedWithStringAsText, (await FindAsync(new VariantComparedWithStringAsTextAnalyzer(), "", body)).Single().Id);
        Assert.IsEmpty(await FindAsync(new StringComparedWithANumberAsADoubleAnalyzer(), "", body));
    }

    // ---- where a finding is: at the operator it is about ----

    private static (int, int, int, int)[] RangesOf(IEnumerable<AnalyzerFinding> findings)
        => [.. findings.Select(finding => (finding.Range.Start.Line, finding.Range.Start.Character, finding.Range.End.Line, finding.Range.End.Character))];

    // `+` nests to the left: both operations of the chain begin at "a ", and each is located at its own operator.
    [TestMethod]
    [DataRow("r = \"a \" + s + \" b\"", 9, 13, DisplayName = "assigned")]
    [DataRow("Debug.Print \"a \" + s + \" b\"", 17, 21, DisplayName = "printed")]
    [DataRow("r = (\"a \" + s) + \" b\"", 10, 15, DisplayName = "the first operation in parentheses")]
    public async Task EachAdditionOfAChainOfStrings_IsReportedAtItsOperator(string statement, int first, int second)
    {
        var findings = await FindAsync(new AmbiguousConcatenationAnalyzer(), "ByVal s As String", "Dim r As String", statement);

        CollectionAssert.AreEquivalent(new[] { (4, first, 4, first + 1), (4, second, 4, second + 1) }, RangesOf(findings));
    }

    [TestMethod]
    public async Task EachLogicalOperatorOfAChainOfNumbers_IsReportedAtItsOperator()
    {
        var findings = await FindAsync(new BitwiseSemanticsAnalyzer(), "ByVal a As Long, ByVal b As Long, ByVal c As Long", "Dim r As Long", "r = a And b And c");

        CollectionAssert.AreEquivalent(new[] { (4, 6, 4, 9), (4, 12, 4, 15) }, RangesOf(findings));
    }

    [TestMethod]
    public async Task AnOperatorOnAContinuedLine_IsReportedOnThatLine()
    {
        var findings = await FindAsync(new AmbiguousConcatenationAnalyzer(), "ByVal s As String, ByVal t As String", "Dim r As String", "r = s _", "    + t");

        CollectionAssert.AreEqual(new[] { (5, 4, 5, 5) }, RangesOf(findings));
    }

    [TestMethod]
    [DataRow("r = a < b", 6, 7, DisplayName = "a comparison")]
    [DataRow("r = 1 = 1 And a < b", 16, 17, DisplayName = "a comparison that is the right operand")]
    public async Task AComparison_IsReportedAtItsOperator(string statement, int start, int end)
    {
        var findings = await FindAsync(new VariantNumberStringComparisonAnalyzer(), "", "Dim a As Variant, b As Variant, r As Boolean", "a = 5", "b = \"4\"", statement);

        CollectionAssert.AreEqual(new[] { (6, start, 6, end) }, RangesOf(findings));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.6 Binary 'Like' Operator")]
    public async Task ALikePatternDeparture_IsReportedAtTheOperator()
    {
        var findings = await FindAsync(new LikePatternDepartureAnalyzer(), "", "Dim r As Variant", "r = \"a\" Like \"[]\"");

        CollectionAssert.AreEqual(new[] { (4, 8, 4, 12) }, RangesOf(findings));
    }

    // the operator locates a diagnostic; a run-time error of the operation stays located at the operation, where Erl and the stack trace say it is. The
    // operands are literals: an error is stated of an operation whose operands are known.
    [TestMethod]
    public async Task TheErrorOfAnOperation_IsLocatedAtTheOperation_AndItsOperatorAtTheToken()
    {
        var source = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nDim r As Long\r\nr = 0\r\nr = 1 + 5 \\ 0\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], source);

        var division = payload.Modules.Single().Procedures.Single().Runtime!.Operations.Single(op => op.Operator == RDCore.SDK.Model.Tokens.IntegerDivisionOp);

        Assert.IsNotNull(division.Error);
        Assert.AreEqual((5, 8, 5, 13), (division.Location.Range.Start.Line, division.Location.Range.Start.Character, division.Location.Range.End.Line, division.Location.Range.End.Character));
        Assert.AreEqual(division.Location, division.Error.Location);
        Assert.IsNotNull(division.OperatorLocation);
        Assert.AreEqual((5, 10, 5, 11), (division.OperatorLocation.Value.Range.Start.Line, division.OperatorLocation.Value.Range.Start.Character,
            division.OperatorLocation.Value.Range.End.Line, division.OperatorLocation.Value.Range.End.Character));
    }
}
