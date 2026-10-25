using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.2.3</strong> The start, end and step of a <c>For</c> loop are numbers: a <c>String</c> that reads as one is the number it reads as,
/// and the loop counts in one numeric type, which the counter's declared type is, or which a <c>Variant</c> counter takes from the start, the end and the step.
/// </summary>
/// <remarks>
/// Each expected value is what MS-VBA printed for the same loop, in Excel (64-bit, en-US). Every loop stops itself after 1000 iterations, so that a loop
/// that would not end fails the test rather than hanging it.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.2.3 For Statement")]
public sealed class ForLoopBoundsTests
{
    // runs `For <counter> = <clause>`, and prints how many times its body ran, then the counter, then the counter's type; or the error the loop raised.
    private static async Task<string> Loop(string declaration, string setup, string counter, string clause)
    {
        var printed = await RunAsync([], string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Public Sub Main()",
            "    Dim c As Long, n As Variant",
            $"    {declaration}",
            "    On Error GoTo Failed",
            $"    {setup}",
            $"    For {counter} = {clause}",
            "        c = c + 1",
            "        If c > 1000 Then Exit For",
            "    Next",
            $"    Debug.Print c & \" \" & {counter} & \" \" & TypeName({counter})",
            "    Exit Sub",
            "Failed:",
            "    Debug.Print \"err \" & Err.Number",
            "End Sub",
            string.Empty));

        Assert.HasCount(1, printed, string.Join(" | ", printed));
        return printed[0];
    }

    [TestMethod]
    // FIZZBUZZ.RDC with N = InputBox("N?") answered 40: the end of the loop is the String "40", which a Variant counter counts to as a number.
    public async Task AStringEndThatReadsAsANumber_IsThatNumber()
        => Assert.AreEqual("40 41 Double", await Loop("Dim i As Variant", "n = \"40\"", "i", "1 To n"));

    [TestMethod]
    [DataRow("n = \"40\"", "1 To n", "40 41 Double")]
    [DataRow("n = \"3\"", "\"1\" To n", "3 4 Double")]
    [DataRow("n = \"2\"", "1 To 9 Step n", "5 11 Double")]
    [DataRow("n = \"1e1\"", "1 To n", "10 11 Double")]
    [DataRow("n = \"2.5\"", "1 To n", "2 3 Double")]
    [DataRow("n = Empty", "1 To n", "0 1 Integer")]
    [DataRow("n = True", "1 To n Step -1", "3 -2 Integer")]
    [DataRow("n = 3", "1 To 2.6", "2 3 Double")]
    public Task AVariantCounter(string setup, string clause, string expected) => AssertLoop("Dim i As Variant", setup, "i", clause, expected);

    [TestMethod]
    // the bounds and the step are Let-coerced to the counter's declared type: 2.6 is an Integer 3, and 1.6 an Integer 2.
    [DataRow("Dim k As Long", "n = \"40\"", "1 To n", "40 41 Long")]
    [DataRow("Dim k As Integer", "n = 0", "1 To 2.6", "3 4 Integer")]
    [DataRow("Dim k As Integer", "n = 0", "1 To 3 Step 1.6", "2 5 Integer")]
    [DataRow("Dim k As Double", "n = 0", "1 To 2 Step 0.5", "3 2.5 Double")]
    public Task ATypedCounter(string declaration, string setup, string clause, string expected) => AssertLoop(declaration, setup, "k", clause, expected);

    [TestMethod]
    // MS-VBAL 5.4.2.3: "If the value of <start-value>, <end-value>, and <step-increment> are not Let-coercible to Double, error 13 (Type mismatch) is raised
    // immediately" - and Null raises the error Let-coercing it raises, 94.
    [DataRow("n = \"abc\"", "1 To n", "err 13")]
    [DataRow("n = \"abc\"", "n To 3", "err 13")]
    [DataRow("n = \"abc\"", "1 To 3 Step n", "err 13")]
    [DataRow("n = Null", "1 To n", "err 94")]
    [DataRow("n = CVErr(5)", "1 To n", "err 13")]
    [DataRow("n = Array(1)", "1 To n", "err 13")]
    // a Variant counter counts in a Date, and "1" is no date - a loop does not read it as the number a Let-coercion to Date would.
    [DataRow("n = \"1\"", "n To CDate(3)", "err 13")]
    [DataRow("n = \"1/1/1900\"", "n To CDate(3)", "2 1/3/1900 Date")]
    public Task AnOperandThatIsNoNumber(string setup, string clause, string expected) => AssertLoop("Dim i As Variant", setup, "i", clause, expected);

    [TestMethod]
    // the type a Variant counter counts in: the widest of the start's, the end's and the step's - Byte, Integer (a Boolean too), Long, LongLong, Double (a
    // Single and a String too), Currency, then Date and Decimal, of which the end's wins over the start's. Empty is none, and the Step 1 a loop that writes
    // none has is not one either.
    [DataRow("CByte(1) To CByte(3) Step CByte(1)", "Byte")]
    [DataRow("CByte(1) To CByte(3)", "Byte")]
    [DataRow("Empty To Empty", "Byte")]
    [DataRow("False To 3", "Integer")]
    [DataRow("True To CByte(3)", "Integer")]
    [DataRow("1 To 3", "Integer")]
    [DataRow("CLng(1) To 3", "Long")]
    [DataRow("CSng(1) To CSng(3) Step CSng(1)", "Double")]
    [DataRow("1 To 3 Step CSng(1)", "Double")]
    [DataRow("CLngLng(1) To 3", "LongLong")]
    [DataRow("CLngLng(1) To CSng(3)", "Double")]
    [DataRow("CLngLng(1) To CCur(3)", "Currency")]
    [DataRow("1# To CCur(3)", "Currency")]
    [DataRow("\"1\" To CCur(3)", "Currency")]
    [DataRow("CCur(1) To CDate(3)", "Date")]
    [DataRow("CDate(1) To 3#", "Date")]
    [DataRow("CDec(1) To CCur(3)", "Decimal")]
    [DataRow("CDec(1) To 3#", "Decimal")]
    [DataRow("CDate(1) To CDec(3)", "Decimal")]
    [DataRow("CDec(1) To CDate(3)", "Date")]
    [DataRow("CDec(1) To CDec(3) Step CDate(1)", "Decimal")]
    public async Task TheTypeAVariantCounterCountsIn(string clause, string expected)
    {
        var printed = await RunAsync([], string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Public Sub Main()",
            "    Dim i As Variant",
            $"    For i = {clause}",
            "        Debug.Print TypeName(i)",
            "        Exit For",
            "    Next",
            "End Sub",
            string.Empty));

        Assert.AreEqual(expected, string.Join(" | ", printed));
    }

    private static async Task AssertLoop(string declaration, string setup, string counter, string clause, string expected)
        => Assert.AreEqual(expected, await Loop(declaration, setup, counter, clause), $"For {counter} = {clause}, after {setup}");
}
