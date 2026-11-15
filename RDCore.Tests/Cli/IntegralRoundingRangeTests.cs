using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL 5.5.1.2.1: a floating-point or fixed-point value converted to an integral type is rounded first, by Banker's rounding, and the whole number it
/// rounds to is the one that must be within the type's range - by a C* function and by an implicit Let-coercion alike, in either engine.
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA printed for the same code in Excel (64-bit, en-US, oracle\s58 K01, K02), or follows from it where a row says so.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.2.1 Let-coercion between numeric types")]
public sealed class IntegralRoundingRangeTests
{
    private static string Program(string setup, string printed) => string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Public Sub Main()",
        "    On Error Resume Next",
        "    " + string.Join("\r\n    ", setup.Split(" ;; ")),
        $"    Debug.Print {printed}",
        "    Debug.Print \"e\" & Err.Number",
        "End Sub",
        string.Empty);

    [TestMethod]
    [DataRow("Dim d As Double ;; d = 32767.3", "CInt(d)", "32767|e0", DisplayName = "K01 CInt of 32767.3")]
    [DataRow("Dim d As Double ;; d = 32767.5", "CInt(d)", "e6", DisplayName = "K01 CInt of 32767.5")]
    [DataRow("Dim d As Double ;; d = -32768.5", "CInt(d)", "-32768|e0", DisplayName = "K01 CInt of -32768.5")]
    [DataRow("Dim d As Double ;; d = -32768.6", "CInt(d)", "e6", DisplayName = "K01 CInt of -32768.6")]
    [DataRow("Dim d As Double ;; d = 255.4", "CByte(d)", "255|e0", DisplayName = "K01 CByte of 255.4")]
    [DataRow("Dim d As Double ;; d = 255.5", "CByte(d)", "e6", DisplayName = "K01 CByte of 255.5")]
    [DataRow("Dim d As Double ;; d = -0.5", "CByte(d)", "0|e0", DisplayName = "K01 CByte of -0.5")]
    [DataRow("Dim d As Double ;; d = -0.6", "CByte(d)", "e6", DisplayName = "K01 CByte of -0.6")]
    [DataRow("Dim d As Double ;; d = 2147483647.4", "CLng(d)", "2147483647|e0", DisplayName = "K01 CLng of 2147483647.4")]
    [DataRow("Dim d As Double ;; d = -2147483648.5", "CLng(d)", "-2147483648|e0", DisplayName = "K01 CLng of -2147483648.5")]
    // as K01's rule has it: 2147483647.5 rounds to 2147483648, which no Long holds, and -2147483648.6 to -2147483649.
    [DataRow("Dim d As Double ;; d = 2147483647.5", "CLng(d)", "e6", DisplayName = "CLng of 2147483647.5")]
    [DataRow("Dim d As Double ;; d = -2147483648.6", "CLng(d)", "e6", DisplayName = "CLng of -2147483648.6")]
    [DataRow("", "CInt(\"-32768.5\")", "-32768|e0", DisplayName = "K02 CInt of the String -32768.5")]
    [DataRow("", "CInt(\"32767.3\")", "32767|e0", DisplayName = "K02 CInt of the String 32767.3")]
    [DataRow("Dim c As Currency ;; c = -32768.5", "CInt(c)", "-32768|e0", DisplayName = "K02 CInt of the Currency -32768.5")]
    [DataRow("Dim c As Currency ;; c = 32767.4", "CInt(c)", "32767|e0", DisplayName = "K02 CInt of the Currency 32767.4")]
    [DataRow("Dim s As Single ;; s = -32768.5", "CInt(s)", "-32768|e0", DisplayName = "K02 CInt of the Single -32768.5")]
    // as K01 and K02's rule has it, from a Decimal, a Date, a String with an exponent, and to a LongLong at its ends.
    [DataRow("Dim v As Variant ;; v = CDec(\"-32768.5\")", "CInt(v)", "-32768|e0", DisplayName = "CInt of the Decimal -32768.5")]
    [DataRow("Dim v As Variant ;; v = CDec(\"32767.5\")", "CInt(v)", "e6", DisplayName = "CInt of the Decimal 32767.5")]
    [DataRow("Dim t As Date ;; t = CDate(32766.5)", "CInt(t)", "32766|e0", DisplayName = "CInt of the Date of serial 32766.5")]
    [DataRow("Dim t As Date ;; t = CDate(32767.5)", "CInt(t)", "e6", DisplayName = "CInt of the Date of serial 32767.5")]
    [DataRow("", "CInt(\"-3.27685E4\")", "-32768|e0", DisplayName = "CInt of the String -3.27685E4")]
    [DataRow("", "CInt(\"3.27675E4\")", "e6", DisplayName = "CInt of the String 3.27675E4")]
    // a Decimal with decimal places is made a LongLong of its Double, which is one only below 2^62 in magnitude (observed in Excel).
    [DataRow("", "CLngLng(CDec(\"9223372036854775806.5\"))", "e6", DisplayName = "CLngLng of the Decimal 9223372036854775806.5")]
    [DataRow("", "CLngLng(CDec(\"4611686018427387903.5\"))", "e6", DisplayName = "CLngLng of the Decimal 4611686018427387903.5")]
    [DataRow("", "CLngLng(CDec(\"9223372036854775807\"))", "9223372036854775807|e0", DisplayName = "CLngLng of the Decimal 9223372036854775807")]
    [DataRow("", "CLngLng(4611686018427387904#)", "4611686018427387904|e0", DisplayName = "CLngLng of the Double 2^62")]
    [DataRow("", "CLngLng(CDec(\"9223372036854775807.5\"))", "e6", DisplayName = "CLngLng of the Decimal 9223372036854775807.5")]
    [DataRow("", "CLngLng(CDec(\"-9223372036854775808.5\"))", "e6", DisplayName = "CLngLng of the Decimal -9223372036854775808.5")]
    [DataRow("", "CLngLng(CCur(\"922337203685477.5\"))", "922337203685478|e0", DisplayName = "CLngLng of the Currency 922337203685477.5")]
    [DataRow("", "CLngLng(CCur(\"-922337203685476.5\"))", "-922337203685476|e0", DisplayName = "CLngLng of the Currency -922337203685476.5")]
    [DataRow("", "CInt(1E+300)", "e6", DisplayName = "CInt of 1E300")]
    [DataRow("", "CLngLng(1E+300)", "e6", DisplayName = "CLngLng of 1E300")]
    public async Task AConversionFunction_RoundsBeforeTheRangeIsChecked(string setup, string printed, string expected)
        => await AssertPrintedInEitherEngineAsync(setup, printed, expected);

    [TestMethod]
    [DataRow("Dim e As Double ;; e = -32768.5 ;; Dim i As Integer ;; i = e", "i", "-32768|e0", DisplayName = "K02 an Integer of the Double -32768.5")]
    [DataRow("Dim e As Double ;; e = 255.4 ;; Dim b As Byte ;; b = e", "b", "255|e0", DisplayName = "K02 a Byte of the Double 255.4")]
    // as K01 and K02's rule has it, for an implicit Let as for the C* function.
    [DataRow("Dim e As Double ;; e = 32767.5 ;; Dim i As Integer ;; i = e", "i", "0|e6", DisplayName = "an Integer of the Double 32767.5")]
    [DataRow("Dim e As Double ;; e = -0.5 ;; Dim b As Byte ;; b = e", "b", "0|e0", DisplayName = "a Byte of the Double -0.5")]
    [DataRow("Dim e As Double ;; e = 255.5 ;; Dim b As Byte ;; b = 1 ;; b = e", "b", "1|e6", DisplayName = "a Byte of the Double 255.5")]
    [DataRow("Dim s As Single ;; s = 255.5 ;; Dim b As Byte ;; b = 1 ;; b = s", "b", "1|e6", DisplayName = "a Byte of the Single 255.5")]
    [DataRow("Dim e As Double ;; e = -2147483648.5 ;; Dim n As Long ;; n = e", "n", "-2147483648|e0", DisplayName = "a Long of the Double -2147483648.5")]
    [DataRow("Dim e As Double ;; e = 2147483647.5 ;; Dim n As Long ;; n = e", "n", "0|e6", DisplayName = "a Long of the Double 2147483647.5")]
    [DataRow("Dim c As Currency ;; c = -0.5 ;; Dim b As Byte ;; b = c", "b", "0|e0", DisplayName = "a Byte of the Currency -0.5")]
    [DataRow("Dim c As Currency ;; c = -32768.5 ;; Dim i As Integer ;; i = c", "i", "-32768|e0", DisplayName = "an Integer of the Currency -32768.5")]
    [DataRow("Dim c As Currency ;; c = 2147483647.5 ;; Dim n As Long ;; n = c", "n", "0|e6", DisplayName = "a Long of the Currency 2147483647.5")]
    [DataRow("Dim v As Variant ;; v = -32768.5 ;; Dim i As Integer ;; i = v", "i", "-32768|e0", DisplayName = "an Integer of a Variant Double -32768.5")]
    [DataRow("Dim v As Variant ;; v = \"-0.5\" ;; Dim b As Byte ;; b = v", "b", "0|e0", DisplayName = "a Byte of a Variant String -0.5")]
    [DataRow("Dim v As Variant ;; v = CDec(\"-32768.5\") ;; Dim i As Integer ;; i = v", "i", "-32768|e0", DisplayName = "an Integer of a Variant Decimal -32768.5")]
    [DataRow("Dim t As Date ;; t = CDate(-0.5) ;; Dim b As Byte ;; b = t", "b", "0|e0", DisplayName = "a Byte of the Date of serial -0.5")]
    [DataRow("Dim v As Variant ;; v = CDec(\"9223372036854775807.5\") ;; Dim n As LongLong ;; n = v", "n", "0|e6", DisplayName = "a LongLong of a Variant Decimal 9223372036854775807.5")]
    [DataRow("Dim e As Double ;; e = 1E+300 ;; Dim i As Integer ;; i = e", "i", "0|e6", DisplayName = "an Integer of the Double 1E300")]
    public async Task AnImplicitLetCoercion_RoundsBeforeTheRangeIsChecked(string setup, string printed, string expected)
        => await AssertPrintedInEitherEngineAsync(setup, printed, expected);

    // the coercion of a compiled procedure: its parameter's Double assigned to its result, of an integral type.
    [TestMethod]
    [DataRow("Integer", "-32768.5", "-32768|e0", DisplayName = "an Integer of -32768.5")]
    [DataRow("Integer", "32767.3", "32767|e0", DisplayName = "an Integer of 32767.3")]
    [DataRow("Integer", "32767.5", "e6", DisplayName = "an Integer of 32767.5")]
    [DataRow("Byte", "-0.5", "0|e0", DisplayName = "a Byte of -0.5")]
    [DataRow("Byte", "255.5", "e6", DisplayName = "a Byte of 255.5")]
    [DataRow("Long", "-2147483648.5", "-2147483648|e0", DisplayName = "a Long of -2147483648.5")]
    [DataRow("Long", "2147483647.5", "e6", DisplayName = "a Long of 2147483647.5")]
    public async Task ACompiledLetCoercion_RoundsBeforeTheRangeIsChecked(string type, string value, string expected)
    {
        CompiledExecutionTests.RequirePayloads();
        var program = string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            $"Private Function Whole(ByVal d As Double) As {type}",
            "    Whole = d",
            "End Function",
            "Public Sub Main()",
            "    On Error Resume Next",
            $"    Debug.Print Whole({value})",
            "    Debug.Print \"e\" & Err.Number",
            "End Sub",
            string.Empty);

        await InspectAsync([], program, (provider, result, _) =>
        {
            var observed = string.Join("|", result.Output.Select(line => line.Trim()));
            Assert.AreEqual(expected, observed, $"which printed {observed}");
            Assert.IsTrue(provider.Image.Compiler.Results.Any(compiled => compiled.Procedure.Name == "Whole" && compiled.Result.Body is not null), "Whole runs compiled");
            return Task.CompletedTask;
        }, engine: VBExecutionEngine.Compiled);
    }

    private static async Task AssertPrintedInEitherEngineAsync(string setup, string printed, string expected)
    {
        var program = Program(setup, printed);
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var observed = string.Join("|", await RunAsync([], program, engine));
            Assert.AreEqual(expected, observed, $"{engine}, which printed {observed}");
        }
    }
}
