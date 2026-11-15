using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.6.9.3.1: the negation of the least <c>Long</c>, -2147483648, which no <c>Long</c> holds the opposite of. MS-VBA negates the one a constant
/// expression evaluates to as the <c>Long</c> it is, and overflows (6) on any other.
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA printed, or reported, for the same code in Excel (64-bit, en-US).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators")]
public sealed class LeastLongNegationTests
{
    private static string Program(string body, params string[] declarations) => StandardModule("Program",
        [
            .. declarations,
            "Public Sub Main()",
            "    On Error Resume Next",
            "    " + string.Join("\r\n    ", body.Split(" ;; ")),
            "    Debug.Print \"e\" & Err.Number",
            "End Sub",
        ]);

    [TestMethod]
    [DataRow("Debug.Print -&H80000000 ;; Debug.Print TypeName(-&H80000000)", "-2147483648|Long|e0", DisplayName = "N07 -&H80000000")]
    [DataRow("Debug.Print -&H80000000&", "-2147483648|e0", DisplayName = "N07 -&H80000000&")]
    [DataRow("Debug.Print -(&H80000000)", "-2147483648|e0", DisplayName = "N10 -(&H80000000)")]
    [DataRow("Debug.Print -(-2147483647 - 1)", "-2147483648|e0", DisplayName = "N10 -(-2147483647 - 1)")]
    [DataRow("Debug.Print -&H80000000 - 1", "e6", DisplayName = "N10 -&H80000000 - 1")]
    [DataRow("Debug.Print -CLng(&H80000000) ;; Debug.Print -CLng(-2147483647 - 1)", "-2147483648|-2147483648|e0", DisplayName = "s60 N17 -CLng(...)")]
    [DataRow("Debug.Print Abs(CLng(-2147483647) - 1) ;; Debug.Print Abs(&H80000000) ;; Debug.Print Abs(-2147483647 - 1)", "-2147483648|-2147483648|-2147483648|e0",
        DisplayName = "s60 A01 Abs of a constant expression")]
    public async Task TheLeastLong_OfAConstantExpression_IsItsOwnNegation(string body, string expected)
        => await AssertPrintedInEitherEngineAsync(Program(body), expected);

    [TestMethod]
    [DataRow("Dim l As Long ;; l = &H80000000 ;; Debug.Print -l", "e6", DisplayName = "N10 -l")]
    [DataRow("Dim l As Long ;; l = &H80000000 ;; Debug.Print Abs(l)", "e6", DisplayName = "N10 Abs(l)")]
    [DataRow("Dim l As Long ;; l = CLng(-2147483647) - 1 ;; Dim v As Variant ;; v = l ;; Debug.Print Abs(v) ;; Debug.Print TypeName(Abs(v))", "2147483648|Double|e0",
        DisplayName = "s60 A03 Abs of a Variant")]
    [DataRow("Dim l As Long ;; l = &H80000000 ;; Debug.Print l * -1", "e6", DisplayName = "N10 l * -1")]
    [DataRow("Dim l As Long ;; l = &H80000000 ;; Debug.Print 0 - l", "e6", DisplayName = "N10 0 - l")]
    [DataRow("Dim i As Integer ;; i = &H8000 ;; Debug.Print -i", "e6", DisplayName = "N10 -i")]
    [DataRow("Debug.Print -&H8000", "e6", DisplayName = "N10 -&H8000, the least Integer")]
    [DataRow("Debug.Print -&H8000000000000000^", "e6", DisplayName = "N07 -&H8000000000000000^, the least LongLong")]
    [DataRow("Dim v As Variant ;; v = CLng(&H80000000) ;; Debug.Print -v ;; Debug.Print TypeName(-v)", "2147483648|Double|e0", DisplayName = "N10 -v, a Variant")]
    public async Task TheLeastOfAnyOtherNegation_IsAnOverflow(string body, string expected)
        => await AssertPrintedInEitherEngineAsync(Program(body), expected);

    [TestMethod]
    [DataRow("Private Const K = -&H80000000", "-2147483648|Long|e0", DisplayName = "N08 Const K = -&H80000000")]
    [DataRow("Private Const K = -(-2147483647 - 1)", "-2147483648|Long|e0", DisplayName = "N12 Const K = -(-2147483647 - 1)")]
    [DataRow("Private Const M = &H80000000 ;; Private Const K = -M", "-2147483648|Long|e0", DisplayName = "s60 N16 Const K = -M")]
    public async Task AConstant_OfTheLeastLongNegated_IsTheLeastLong(string declarations, string expected)
        => await AssertPrintedInEitherEngineAsync(Program("Debug.Print K ;; Debug.Print TypeName(K)", declarations.Split(" ;; ")), expected);

    // a named constant: of a Long, the Long; of a Variant, a Variant, whose negation is the Double (observed in Excel).
    [TestMethod]
    [DataRow("Private Const M = &H80000000", "Debug.Print -M ;; Debug.Print TypeName(-M) ;; Debug.Print -(M)", "-2147483648|Long|-2147483648|e0", DisplayName = "s60 N13 -M")]
    [DataRow("Private Const M = &H80000000", "Debug.Print -M - 1", "e6", DisplayName = "s60 N13 -M - 1")]
    [DataRow("Private Const M As Variant = &H80000000", "Debug.Print -M ;; Debug.Print TypeName(-M)", "2147483648|Double|e0", DisplayName = "s60 N14 a Variant constant")]
    [DataRow("Private Const I = &H8000", "Debug.Print -I", "e6", DisplayName = "s60 N15 an Integer constant")]
    [DataRow("Private Const K = &H80000000", "Debug.Print Abs(K)", "-2147483648|e0", DisplayName = "s60 A02 Abs(K)")]
    public async Task ANamedConstant_IsAConstantExpression(string declaration, string body, string expected)
        => await AssertPrintedInEitherEngineAsync(Program(body, declaration), expected);

    [TestMethod]
    [DataRow("Private Const K = -&H8000", DisplayName = "N11 Const K = -&H8000")]
    [DataRow("Private Const K = -&H8000000000000000^", DisplayName = "N09 Const K = -&H8000000000000000^")]
    public async Task AConstant_OfTheLeastIntegerOrLongLongNegated_IsAnOverflow(string declaration)
        => StringAssert.Contains(string.Join(" / ", await LoadErrorsAsync([], Program("Debug.Print K", declaration))), "Overflow");

    private static async Task AssertPrintedInEitherEngineAsync(string program, string expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var observed = string.Join("|", (await RunAsync([], program, engine)).Select(line => line.Trim()));
            Assert.AreEqual(expected, observed, $"{engine}, which printed {observed}");
        }
    }
}
