using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A scalar variable assigned an expression of them, and a condition of them, are evaluated on payloads: the same values, and the same
/// errors, as the boxed evaluation - which the suite is also run with (<c>RDCORE_UNBOXED=0</c>), for these to hold both ways.
/// </summary>
[TestClass]
public sealed class UnboxedEvaluationTests
{
    private static string Program(params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("Dim l As Long, i As Integer ;; i = 3 ;; l = 2147483647 ;; l = l - i ;; Debug.Print l", "2147483644", DisplayName = "a Long less an Integer")]
    [DataRow("Dim d As Double, l As Long ;; d = 0.5 ;; l = 2 ;; d = d + l ;; Debug.Print d", "2.5", DisplayName = "a Double plus a Long")]
    [DataRow("Dim d As Double ;; d = 7 ;; d = d / 2 ;; Debug.Print d", "3.5", DisplayName = "a Double divided")]
    [DataRow("Dim s As Single ;; s = 0.5 ;; s = s * 3 ;; Debug.Print s", "1.5", DisplayName = "a Single multiplied")]
    [DataRow("Dim i As Integer ;; i = 5 ;; i = -i ;; Debug.Print i", "-5", DisplayName = "an Integer negated")]
    [DataRow("Dim ll As LongLong ;; ll = 4000000000^ ;; ll = ll * 2 ;; Debug.Print ll", "8000000000", DisplayName = "a LongLong multiplied")]
    [DataRow("Dim i As Integer, l As Long ;; l = 300 ;; i = l + 1 ;; Debug.Print i", "301", DisplayName = "a Long in the range of an Integer, assigned to one")]
    [DataRow("Dim b As Byte ;; b = 200 ;; b = b + 50 ;; Debug.Print b", "250", DisplayName = "a Byte that fits")]
    [DataRow("Dim l As Long ;; l = -17 ;; l = l Mod 5 ;; Debug.Print l", "-2", DisplayName = "a Long Mod, with the dividend's sign")]
    [DataRow("Dim l As Long, i As Long ;; l = 10 ;; i = 3 ;; l = (l + i) * 2 ;; Debug.Print l", "26", DisplayName = "an expression in parentheses")]
    [DataRow("Dim l As Long ;; l = -17 ;; l = l \\ 5 ;; Debug.Print l", "-3", DisplayName = "a Long integer-divided, truncated")]
    [DataRow("Dim l As Long, d As Double ;; d = 7.5 ;; l = d \\ 2 ;; Debug.Print l", "4", DisplayName = "a Double integer-divided, rounded first (boxed)")]
    public async Task WhatIsAssignedOnPayloads_IsWhatTheBoxedEvaluationAssigns(string body, string printed)
        => Assert.AreEqual(printed, (await RunAsync([], Program(body.Split(" ;; ")))).Last());

    // the errors are the boxed evaluation's: the payload evaluation declines what would raise one, and the expression is evaluated again, boxed.
    [TestMethod]
    [DataRow("Dim l As Long ;; l = 2147483647 ;; l = l + 1 ;; Debug.Print Err.Number & \" \" & l", "6 2147483647", DisplayName = "a Long past its range")]
    [DataRow("Dim i As Integer, l As Long ;; l = 40000 ;; i = l ;; Debug.Print Err.Number & \" \" & i", "6 0", DisplayName = "a Long past the range of an Integer, assigned to one")]
    [DataRow("Dim i As Integer ;; i = -32768 ;; i = -i ;; Debug.Print Err.Number & \" \" & i", "6 -32768", DisplayName = "the least Integer negated")]
    [DataRow("Dim b As Byte ;; b = 200 ;; b = b + 100 ;; Debug.Print Err.Number & \" \" & b", "6 200", DisplayName = "a Byte past its range")]
    [DataRow("Dim d As Double ;; d = 1 ;; d = d / 0 ;; Debug.Print Err.Number & \" \" & d", "11 inf", DisplayName = "a Double divided by zero, its infinity assigned (MS-VBA)")]
    [DataRow("Dim l As Long ;; l = 7 ;; l = l Mod 0 ;; Debug.Print Err.Number & \" \" & l", "11 7", DisplayName = "a Long Mod zero")]
    [DataRow("Dim l As Long ;; l = 7 ;; l = l \\ 0 ;; Debug.Print Err.Number & \" \" & l", "11 7", DisplayName = "a Long integer-divided by zero")]
    [DataRow("Dim ll As LongLong ;; ll = 9223372036854775807^ ;; ll = ll + 1 ;; Debug.Print Err.Number & \" \" & ll", "6 9223372036854775807", DisplayName = "a LongLong past its range")]
    public async Task WhatWouldRaiseAnError_RaisesTheBoxedEvaluationsError(string body, string printed)
        => Assert.AreEqual(printed, (await RunAsync([], Program(body.Split(" ;; ")))).Last());

    // MS-VBAL §5.4.2.3, stepped on payloads: the counter after the loop is the first value past the end.
    [TestMethod]
    [DataRow("Dim i As Long, n As Long ;; For i = 1 To 10 ;; n = n + i ;; Next ;; Debug.Print n & \" \" & i", "55 11", DisplayName = "a Long counter")]
    [DataRow("Dim i As Integer, n As Long ;; For i = 10 To 1 Step -3 ;; n = n + i ;; Next ;; Debug.Print n & \" \" & i", "22 -2", DisplayName = "an Integer counter, stepping down")]
    [DataRow("Dim d As Double, n As Long ;; For d = 0 To 2 Step 0.5 ;; n = n + 1 ;; Next ;; Debug.Print n & \" \" & d", "5 2.5", DisplayName = "a Double counter, a fractional step")]
    [DataRow("Dim i As Long, n As Long ;; For i = 1 To 10 ;; i = i + 1 ;; n = n + 1 ;; Next ;; Debug.Print n & \" \" & i", "5 11", DisplayName = "a counter its body assigns")]
    [DataRow("Dim i As Integer ;; For i = 32766 To 32767 ;; Next ;; Debug.Print Err.Number & \" \" & i", "6 32767", DisplayName = "an Integer counter stepped past its range")]
    [DataRow("Dim v As Variant, n As Long ;; For v = 1 To 5 ;; n = n + v ;; Next ;; Debug.Print n & \" \" & v & \" \" & TypeName(v)", "15 6 Integer", DisplayName = "a Variant counter")]
    [DataRow("Dim v As Variant, n As Long ;; For v = 32765 To 32767 ;; n = n + 1 ;; Next ;; Debug.Print n & \" \" & v & \" \" & TypeName(v)", "3 32768 Long", DisplayName = "a Variant counter widened past its end")]
    [DataRow("Dim v As Variant, n As Long ;; For v = 0 To 1 Step 0.25 ;; n = n + 1 ;; Next ;; Debug.Print n & \" \" & v & \" \" & TypeName(v)", "5 1.25 Double", DisplayName = "a Variant counter, a fractional step")]
    [DataRow("Dim v As Variant, n As Long ;; For v = 10 To 1 Step -3 ;; n = n + v ;; Next ;; Debug.Print n & \" \" & v", "22 -2", DisplayName = "a Variant counter, stepping down")]
    [DataRow("Dim v As Variant, n As Long ;; For v = CByte(250) To CByte(255) ;; n = n + 1 ;; Next ;; Debug.Print n & \" \" & v & \" \" & TypeName(v)", "6 256 Integer", DisplayName = "a Variant Byte counter widened past its end")]
    [DataRow("Dim v As Variant, s As Variant ;; s = 0# ;; For v = 1 To 1000 ;; s = s + v ;; Next ;; Debug.Print s & \" \" & TypeName(s)", "500500 Double", DisplayName = "a Variant sum over a Variant counter")]
    public async Task AForLoop_SteppedOnPayloads_IsWhatTheBoxedEvaluationSteps(string body, string printed)
        => Assert.AreEqual(printed, (await RunAsync([], Program(body.Split(" ;; ")))).Last());

    // MS-VBAL §5.6.9.5 and §5.6.9.8 on payloads, a Boolean being -1 or 0 (recorded in Excel).
    [TestMethod]
    [DataRow("Dim b As Boolean, l As Long ;; l = 3 ;; b = l > 2 ;; Debug.Print b", "True", DisplayName = "a Long compared")]
    [DataRow("Dim b As Boolean, i As Integer, d As Double ;; i = 2 ;; d = 2.5 ;; b = i >= d ;; Debug.Print b", "False", DisplayName = "an Integer compared with a Double, as Doubles")]
    [DataRow("Dim b As Boolean, l As Long ;; l = 5 ;; b = Not (l = 5) ;; Debug.Print b", "False", DisplayName = "a comparison negated")]
    [DataRow("Dim b As Boolean, l As Long ;; l = 5 ;; b = l > 1 And l < 10 ;; Debug.Print b", "True", DisplayName = "two comparisons And-ed")]
    [DataRow("Dim l As Long ;; l = 12 ;; l = l And 10 ;; Debug.Print l", "8", DisplayName = "the bits of a Long And-ed")]
    [DataRow("Dim y As Byte ;; y = 5 ;; y = Not y ;; Debug.Print y", "250", DisplayName = "the 8 bits of a Byte inverted")]
    [DataRow("Dim l As Long, b As Boolean ;; b = True ;; l = b + 1 ;; Debug.Print l", "0", DisplayName = "a Boolean added, as an Integer")]
    [DataRow("Dim b As Boolean, l As Long ;; l = 7 ;; b = l ;; Debug.Print b", "True", DisplayName = "a Long assigned to a Boolean")]
    [DataRow("Dim b As Boolean, s As Single ;; s = 0.1 ;; b = s = 0.1 ;; Debug.Print b", "True", DisplayName = "a Single compared with a Double, as Singles (boxed)")]
    [DataRow("Dim b As Boolean ;; b = True ;; b = b Eqv False ;; Debug.Print b ;; b = False Imp b ;; Debug.Print b", "True", DisplayName = "Eqv and Imp")]
    [DataRow("Dim y As Byte, b As Boolean ;; b = True ;; y = b ;; Debug.Print Err.Number & \" \" & y", "0 255", DisplayName = "True assigned to a Byte (boxed)")]
    [DataRow("Dim b As Boolean, l As Long ;; l = 2 ;; b = l ;; b = Not b ;; Debug.Print b & \" \" & CInt(b)", "False 0", DisplayName = "a Boolean inverted")]
    [DataRow("Dim ll As LongLong, b As Boolean ;; ll = 9007199254740993^ ;; b = ll > 9007199254740992^ ;; Debug.Print b", "True", DisplayName = "two LongLongs no Double tells apart")]
    [DataRow("Dim s As Single, l As Long, b As Boolean ;; s = 16777216 ;; l = 16777217 ;; b = s < l ;; Debug.Print b", "True", DisplayName = "a Single compared with a Long, as Doubles")]
    public async Task AComparisonOrALogicalOperation_OnPayloads_IsWhatTheBoxedEvaluationEvaluates(string body, string printed)
        => Assert.AreEqual(printed, (await RunAsync([], Program(body.Split(" ;; ")))).Last());

    // a Currency as its ten-thousandths, a Date as its serial; what would round, or would not be a date, is the boxed evaluation's (recorded in Excel).
    [TestMethod]
    [DataRow("Dim c As Currency ;; c = 1.5 ;; c = c + 2 ;; Debug.Print c", "3.5", DisplayName = "a Currency plus an Integer")]
    [DataRow("Dim c As Currency ;; c = 922337203685477.5807@ ;; c = c + 1 ;; Debug.Print Err.Number & \" \" & c", "6 922337203685477.5807", DisplayName = "a Currency past its range")]
    [DataRow("Dim c As Currency ;; c = 1.25 ;; c = c * 3 ;; Debug.Print c", "3.75", DisplayName = "a Currency multiplied")]
    [DataRow("Dim c As Currency ;; c = 0.0001 ;; c = c * 0.5 ;; Debug.Print c ;; c = 0.0003 ;; c = c * 0.5 ;; Debug.Print c", "0.0002", DisplayName = "a Currency product rounded to even (boxed)")]
    [DataRow("Dim c As Currency, l As Long ;; c = 2.5 ;; l = c ;; Debug.Print l", "2", DisplayName = "a Currency rounded to a Long (boxed)")]
    [DataRow("Dim c As Currency, l As Long ;; c = 7 ;; l = c * 2 ;; Debug.Print l", "14", DisplayName = "a whole Currency assigned to a Long")]
    [DataRow("Dim c As Currency, b As Boolean ;; c = 0.0001 ;; b = c > 0 ;; Debug.Print b", "True", DisplayName = "a Currency compared")]
    [DataRow("Dim c As Currency, d As Double ;; c = 1.1 ;; d = c ;; Debug.Print d", "1.1", DisplayName = "a Currency assigned to a Double (boxed)")]
    [DataRow("Dim c As Currency, n As Long ;; For c = 0 To 1 Step 0.25 ;; n = n + 1 ;; Next ;; Debug.Print n & \" \" & c", "5 1.25", DisplayName = "a Currency counter")]
    [DataRow("Dim c As Currency ;; c = 3.5 ;; c = -c ;; Debug.Print c", "-3.5", DisplayName = "a Currency negated")]
    [DataRow("Dim c As Currency ;; c = 0.1 ;; c = c * 0.1 ;; Debug.Print c", "0.01", DisplayName = "a Currency multiplied by a Double")]
    [DataRow("Dim c As Currency, i As Integer ;; c = 10 ;; i = 3 ;; c = c - i ;; Debug.Print c & \" \" & TypeName(c - i)", "7 Currency", DisplayName = "a Currency less an Integer")]
    [DataRow("Dim d As Date ;; d = #1/1/2020# ;; d = d + 1 ;; Debug.Print CDbl(d)", "43832", DisplayName = "a Date plus a day")]
    [DataRow("Dim d As Date, e As Date ;; d = #1/1/2020# ;; e = #1/3/2020# ;; Debug.Print e - d & \" \" & TypeName(e - d)", "2 Double", DisplayName = "two Dates subtracted")]
    [DataRow("Dim d As Date, b As Boolean ;; d = #1/1/2020# ;; b = d < #1/2/2020# ;; Debug.Print b", "True", DisplayName = "a Date compared")]
    [DataRow("Dim d As Date ;; d = #12/31/9999# ;; d = d + 1 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "6 2958465", DisplayName = "a Date past 9999")]
    [DataRow("Dim d As Date ;; d = 3000000 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "6 0", DisplayName = "a number past the last date, assigned to a Date")]
    [DataRow("Dim d As Date ;; d = -657434.5 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "0 -657434.5", DisplayName = "noon of the first date")]
    [DataRow("Dim d As Date ;; d = -657435 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "6 0", DisplayName = "a number before the first date")]
    [DataRow("Dim d As Date ;; d = 2958465.9999999 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "0 2958465.9999999", DisplayName = "the last instant of the last date")]
    [DataRow("Dim d As Date ;; d = -657434.9 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "0 -657434.9", DisplayName = "late on the first date")]
    [DataRow("Dim d As Date ;; d = #1/1/100# ;; d = d - 1 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "6 -657434", DisplayName = "a Date before the year 100")]
    [DataRow("Dim d As Date, n As Long ;; For d = #1/1/2020# To #1/5/2020# ;; n = n + 1 ;; Next ;; Debug.Print n & \" \" & CDbl(d)", "5 43836", DisplayName = "a Date counter")]
    [DataRow("Dim d As Date, n As Long ;; d = #1/1/2020# ;; If d Then n = 1 ;; Debug.Print n", "1", DisplayName = "If a Date")]
    [DataRow("Dim d As Date, x As Double ;; x = 0.5 ;; d = #1/1/2020# + x ;; Debug.Print CDbl(d)", "43831.5", DisplayName = "a Date plus a Double")]
    [DataRow("Dim d As Date ;; d = 1 ;; d = -d ;; Debug.Print CDbl(d)", "-1", DisplayName = "a Date negated")]
    public async Task ACurrencyOrADate_OnPayloads_IsWhatTheBoxedEvaluationEvaluates(string body, string printed)
        => Assert.AreEqual(printed, (await RunAsync([], Program(body.Split(" ;; ")))).Last());

    // a Variant that holds a scalar, as a payload of its subtype: a plan made for what it holds, and whatever overflows widened by the boxed
    // evaluation as MS-VBA widens it (recorded in Excel).
    [TestMethod]
    [DataRow("Dim v As Variant ;; v = 5 ;; v = v + 1 ;; Debug.Print v & \" \" & TypeName(v)", "6 Integer", DisplayName = "a Variant Integer plus one")]
    [DataRow("Dim v As Variant ;; v = 32767 ;; v = v + 1 ;; Debug.Print v & \" \" & TypeName(v)", "32768 Long", DisplayName = "a Variant Integer widened to a Long")]
    [DataRow("Dim v As Variant ;; v = 2147483647 ;; v = v + 1 ;; Debug.Print v & \" \" & TypeName(v)", "2147483648 Double", DisplayName = "a Variant Long widened to a Double")]
    [DataRow("Dim v As Variant, d As Double ;; v = 3 ;; d = 0.5 ;; v = v * d ;; Debug.Print v & \" \" & TypeName(v)", "1.5 Double", DisplayName = "a Variant times a Double")]
    [DataRow("Dim v As Variant, l As Long ;; v = 2.5 ;; l = v ;; Debug.Print l", "2", DisplayName = "a Variant Double assigned to a Long (boxed)")]
    [DataRow("Dim v As Variant ;; v = CByte(255) ;; v = v + 1 ;; Debug.Print v & \" \" & TypeName(v)", "256 Integer", DisplayName = "a Variant Byte widened to an Integer")]
    [DataRow("Dim v As Variant ;; v = True ;; v = v + 1 ;; Debug.Print v & \" \" & TypeName(v)", "0 Integer", DisplayName = "a Variant Boolean plus one")]
    [DataRow("Dim v As Variant, n As Long ;; v = 7 ;; If v > 5 Then n = 1 ;; Debug.Print n", "1", DisplayName = "a Variant in a condition")]
    [DataRow("Dim v As Variant ;; v = 10 ;; v = -v ;; Debug.Print v & \" \" & TypeName(v)", "-10 Integer", DisplayName = "a Variant negated")]
    [DataRow("Dim v As Variant ;; v = -32768 ;; v = -v ;; Debug.Print v & \" \" & TypeName(v)", "32768 Long", DisplayName = "the least Variant Integer negated, widened")]
    [DataRow("Dim v As Variant ;; v = \"5\" ;; v = v + 1 ;; Debug.Print v & \" \" & TypeName(v)", "6 Double", DisplayName = "a Variant String plus one (boxed)")]
    [DataRow("Dim v As Variant ;; v = Null ;; v = v + 1 ;; Debug.Print IsNull(v)", "True", DisplayName = "a Variant Null plus one (boxed)")]
    [DataRow("Dim v As Variant ;; v = v + 1 ;; Debug.Print v & \" \" & TypeName(v)", "1 Integer", DisplayName = "an Empty Variant plus one (boxed)")]
    [DataRow("Dim v As Variant, w As Variant ;; v = 10 ;; w = 3 ;; Debug.Print v \\ w & \" \" & v Mod w & \" \" & v / w", "3 1 3.33333333333333", DisplayName = "two Variants divided")]
    [DataRow("Dim v As Variant ;; v = #1/1/2020# ;; v = v + 1 ;; Debug.Print TypeName(v) & \" \" & CDbl(v)", "Date 43832", DisplayName = "a Variant Date plus a day")]
    [DataRow("Dim v As Variant ;; v = 1.5@ ;; v = v * 2 ;; Debug.Print v & \" \" & TypeName(v)", "3 Currency", DisplayName = "a Variant Currency doubled")]
    [DataRow("Dim v As Variant ;; v = CLng(5) ;; v = v + CLng(1) ;; Debug.Print TypeName(v)", "Long", DisplayName = "a Variant Long plus a Long")]
    [DataRow("Dim v As Variant, w As Variant ;; v = 5 ;; w = v ;; v = 6 ;; Debug.Print w", "5", DisplayName = "a Variant assigned is a copy")]
    [DataRow("Dim v As Variant, w As Variant ;; v = 200 ;; w = 200 ;; v = v * w ;; Debug.Print v & \" \" & TypeName(v)", "40000 Long", DisplayName = "two Variant Integers multiplied, widened")]
    [DataRow("Dim v As Variant ;; v = CByte(10) ;; v = Not v ;; Debug.Print v & \" \" & TypeName(v)", "245 Byte", DisplayName = "a Variant Byte inverted")]
    [DataRow("Dim v As Variant ;; v = CByte(200) ;; v = v + CByte(100) ;; Debug.Print v & \" \" & TypeName(v)", "300 Integer", DisplayName = "two Variant Bytes added, widened to an Integer")]
    [DataRow("Dim v As Variant ;; v = CSng(3E+38) ;; v = v * CSng(10) ;; Debug.Print TypeName(v) & \" \" & v", "Double 3.00000000549776E+39", DisplayName = "a Variant Single widened to a Double")]
    public async Task AVariantHoldingAScalar_OnPayloads_IsWhatTheBoxedEvaluationEvaluates(string body, string printed)
        => Assert.AreEqual(printed, (await RunAsync([], Program(body.Split(" ;; ")))).Last());

    // Strings evaluated to the String they are: joined, assigned, compared under the module's comparison mode (recorded in Excel).
    [TestMethod]
    [DataRow("Dim s As String ;; s = \"ab\" ;; s = s & \"cd\" ;; Debug.Print s", "abcd", DisplayName = "a String joined")]
    [DataRow("Dim s As String, t As String ;; s = \"a\" ;; t = s ;; s = \"b\" ;; Debug.Print t", "a", DisplayName = "a String assigned is a copy")]
    [DataRow("Dim s As String, n As Long ;; s = \"abc\" ;; If s = \"abc\" Then n = 1 ;; Debug.Print n", "1", DisplayName = "a String compared in a condition")]
    [DataRow("Dim s As String ;; s = \"B\" ;; Debug.Print (s < \"a\") & \" \" & (s > \"a\")", "True False", DisplayName = "Strings compared by their data")]
    [DataRow("Dim s As String ;; s = \"a\" + \"b\" ;; Debug.Print s", "ab", DisplayName = "two Strings added")]
    [DataRow("Dim s As String * 3 ;; s = \"abcdef\" ;; s = \"a\" & \"b\" ;; Debug.Print s & \"|\"", "ab |", DisplayName = "a fixed-length String assigned (boxed)")]
    [DataRow("Dim s As String, n As Long ;; For n = 1 To 3 ;; s = s & n ;; Next ;; Debug.Print s", "123", DisplayName = "a number joined to a String (boxed)")]
    [DataRow("Dim s As String ;; s = \"x\" ;; Debug.Print s = \"X\"", "False", DisplayName = "Strings that differ in case")]
    [DataRow("Dim s As String, v As Variant ;; v = \"q\" ;; s = v ;; Debug.Print s", "q", DisplayName = "a Variant String assigned to a String (boxed)")]
    public async Task AString_OnItsText_IsWhatTheBoxedEvaluationEvaluates(string body, string printed)
        => Assert.AreEqual(printed, (await RunAsync([], Program(body.Split(" ;; ")))).Last());

    private static (string Name, string Source) Class(string name, params string[] body)
        => (name, $"VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"{name}\"\r\nOption Explicit\r\n{string.Join("\r\n", body)}\r\n");

    // the fields of an object held as payloads, as a local is: each object its own, an overflow the boxed evaluation's error.
    [TestMethod]
    public async Task TheScalarFieldsOfAnObject_OnPayloads_AreItsOwn()
    {
        var counter = Class("Tally", "Private n As Integer", "Public Sub Bump(ByVal by As Integer)", "n = n + by", "End Sub",
            "Public Property Get Value() As Integer", "Value = n", "End Property");
        var program = Program("Dim a As New Tally, b As New Tally", "a.Bump 10", "a.Bump 20", "b.Bump 1", "a.Bump 32767",
            "Debug.Print Err.Number & \" \" & a.Value & \" \" & b.Value");

        Assert.AreEqual("6 30 1", (await RunAsync([counter], program)).Last());
    }

    // a Variant field that held an object lets go of it when a number is assigned to it, which is where the object's Terminate runs.
    [TestMethod]
    public async Task AVariantFieldThatHeldAnObject_LetsItGo_WhenANumberIsAssigned()
    {
        var token = Class("Token", "Private Sub Class_Terminate()", "Debug.Print \"terminated\"", "End Sub");
        var holder = Class("Holder", "Private v As Variant", "Public Sub Take()", "Set v = New Token", "End Sub",
            "Public Sub Drop()", "v = 1", "v = v + 1", "Debug.Print \"dropped \" & v", "End Sub");
        var output = await RunAsync([token, holder], Program("Dim h As New Holder", "h.Take", "h.Drop"));

        CollectionAssert.AreEqual(new[] { "terminated", "dropped 2" }, output.TakeLast(2).ToArray());
    }

    // the variables of a module and Static locals held as payloads, resolved once for each place they are named: a value they keep, an overflow
    // the boxed evaluation's error, and what an End resets them to.
    [TestMethod]
    public async Task AModuleVariableAndAStaticLocal_OnPayloads_KeepTheirValues()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate total As Integer\r\nPrivate v As Variant\r\n"
            + "Private Function Counted() As Long\r\nStatic n As Long\r\nn = n + 1\r\nCounted = n\r\nEnd Function\r\n"
            + "Private Sub Add(ByVal by As Integer)\r\ntotal = total + by\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\nOn Error Resume Next\r\nDim i As Long, c As Long\r\nFor i = 1 To 3\r\nc = Counted()\r\nNext\r\n"
            + "Add 30000\r\nAdd 2000\r\nAdd 1000\r\nv = 5\r\nv = v * total\r\n"
            + "Debug.Print c & \" \" & Err.Number & \" \" & total & \" \" & v & \" \" & TypeName(v)\r\nEnd Sub\r\n";

        Assert.AreEqual("3 6 32000 160000 Long", (await RunAsync([], program)).Last());
    }

    [TestMethod]
    public async Task Strings_ComparedUnderOptionCompareText_IgnoreCase()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nOption Compare Text\r\n"
            + "Public Sub Main()\r\nDim s As String, n As Long\r\ns = \"ABC\"\r\nIf s = \"abc\" Then n = 1\r\nDebug.Print n & \" \" & (s < \"abd\")\r\nEnd Sub\r\n";

        Assert.AreEqual("1 True", (await RunAsync([], program)).Last());
    }

    // the conditions of If, Do, While and Case on payloads; an error in a condition is the boxed evaluation's, and On Error Resume Next goes on into the
    // statement that follows it - the Then of an If (recorded in Excel).
    [TestMethod]
    [DataRow("Dim l As Long, n As Long ;; l = 4 ;; If l Mod 2 = 0 Then n = 1 Else n = 2 ;; Debug.Print n", "1", DisplayName = "If")]
    [DataRow("Dim d As Double, n As Long ;; d = 0.5 ;; If d Then n = 1 ;; Debug.Print n", "1", DisplayName = "If a Double that is not zero")]
    [DataRow("Dim l As Long, n As Long ;; Do While l < 5 ;; l = l + 2 ;; n = n + 1 ;; Loop ;; Debug.Print n & \" \" & l", "3 6", DisplayName = "Do While")]
    [DataRow("Dim l As Long ;; Do ;; l = l + 1 ;; Loop Until l >= 3 ;; Debug.Print l", "3", DisplayName = "Loop Until")]
    [DataRow("Dim l As Long ;; While l < 4 ;; l = l + 1 ;; Wend ;; Debug.Print l", "4", DisplayName = "While")]
    [DataRow("Dim l As Long, n As Long ;; l = 7 ;; Select Case l ;; Case 1 To 5 ;; n = 1 ;; Case Is > 6 ;; n = 2 ;; Case Else ;; n = 3 ;; End Select ;; Debug.Print n", "2", DisplayName = "Case To and Case Is")]
    [DataRow("Dim d As Double, n As Long ;; d = 3 ;; Select Case d ;; Case 2, 3 ;; n = 1 ;; End Select ;; Debug.Print n", "1", DisplayName = "a Double selector, Integer cases")]
    [DataRow("Dim l As Long, n As Long ;; l = 7 ;; Select Case True ;; Case l > 5 ;; n = 1 ;; End Select ;; Debug.Print n", "1", DisplayName = "Select Case True")]
    [DataRow("Dim i As Integer, n As Long ;; i = 3 ;; Select Case i ;; Case 2.5 To 3.5 ;; n = 1 ;; End Select ;; Debug.Print n", "1", DisplayName = "an Integer selector, a range of Doubles")]
    [DataRow("Dim d As Double, n As Long ;; d = 1 ;; If d / 0 > 1 Then n = 1 ;; Debug.Print Err.Number & \" \" & n", "11 1", DisplayName = "a condition divided by zero")]
    [DataRow("Dim i As Integer, n As Long ;; i = 32767 ;; If i + 1 > 0 Then n = 1 ;; Debug.Print Err.Number & \" \" & n", "6 1", DisplayName = "a condition past the range of an Integer")]
    public async Task ACondition_OnPayloads_IsWhatTheBoxedEvaluationDecides(string body, string printed)
        => Assert.AreEqual(printed, (await RunAsync([], Program(body.Split(" ;; ")))).Last());

    // a variable a ByRef parameter is an alias of is the caller's: assigned on payloads through the alias, the caller sees it.
    [TestMethod]
    public async Task AVariableAssignedThroughAByRefParameter_IsTheCallersVariable()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
            + "Private Sub Twice(ByRef N As Long)\r\nN = N * 2\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\nDim L As Long\r\nL = 21\r\nTwice L\r\nL = L + 0\r\nDebug.Print L\r\nEnd Sub\r\n";

        Assert.AreEqual("42", (await RunAsync([], program)).Last());
    }

    // calls: a scalar passed by value evaluated on payloads into the parameter's cell, a function result assigned on payloads, what a call returns
    // written to a scalar variable - and whatever would round, or raise, the boxed evaluation's (recorded in Excel).
    [TestMethod]
    [DataRow("", "Dim l As Long ;; l = Twice(21) ;; Debug.Print l", "42", DisplayName = "a Long passed and returned")]
    [DataRow("", "Dim i As Integer ;; i = Twice(20000) ;; Debug.Print Err.Number & \" \" & i", "6 0", DisplayName = "a result past the range of the variable")]
    [DataRow("", "Dim l As Long ;; l = Twice(2.5) ;; Debug.Print l ;; l = Twice(3.5) ;; Debug.Print l", "8", DisplayName = "a Double rounded to the parameter (boxed)")]
    [DataRow("Function Total(ByVal N As Long) As Long ;; Dim i As Long ;; For i = 1 To N ;; Total = Total + i ;; Next ;; End Function", "Debug.Print Total(10)", "55", DisplayName = "a function result accumulated")]
    [DataRow("Function Half(ByVal N As Double) As Integer ;; Half = N / 2 ;; End Function", "Debug.Print Half(5) & \" \" & Half(7)", "2 4", DisplayName = "a function result rounded to its type (boxed)")]
    [DataRow("Function Big() As Integer ;; Big = 40000 ;; End Function", "Debug.Print Big() ;; Debug.Print Err.Number", "6", DisplayName = "a function result past its range")]
    [DataRow("Function Fact(ByVal N As Long) As Double ;; If N <= 1 Then Fact = 1 Else Fact = N * Fact(N - 1) ;; End Function", "Debug.Print Fact(10)", "3628800", DisplayName = "a recursive function")]
    [DataRow("Sub Bump(ByVal N As Long) ;; N = N + 1 ;; End Sub", "Dim l As Long ;; l = 5 ;; Bump l ;; Debug.Print l", "5", DisplayName = "a parameter passed by value is a copy")]
    [DataRow("Function Flag(ByVal N As Long) As Boolean ;; Flag = N > 3 ;; End Function", "Debug.Print Flag(5) & \" \" & Flag(2)", "True False", DisplayName = "a Boolean function result")]
    [DataRow("Sub Bump(ByRef N As Long) ;; N = N + 1 ;; End Sub", "Dim l As Long ;; l = 5 ;; Bump l ;; Bump l ;; Debug.Print l", "7", DisplayName = "a variable passed by reference")]
    [DataRow("Sub Swap(ByRef A As Long, ByRef B As Long) ;; Dim t As Long ;; t = A ;; A = B ;; B = t ;; End Sub", "Dim x As Long, y As Long ;; x = 1 ;; y = 2 ;; Swap x, y ;; Debug.Print x & \" \" & y", "2 1", DisplayName = "two variables swapped by reference")]
    [DataRow("Sub Both(ByRef A As Long, ByRef B As Long) ;; A = A + 1 ;; B = B * 10 ;; End Sub", "Dim x As Long ;; x = 1 ;; Both x, x ;; Debug.Print x", "20", DisplayName = "one variable passed to two parameters by reference")]
    [DataRow("Sub Inner(ByRef N As Long) ;; N = N + 1 ;; End Sub ;; Sub Outer(ByRef N As Long) ;; Inner N ;; Inner N ;; End Sub", "Dim x As Long ;; Outer x ;; Debug.Print x", "2", DisplayName = "a parameter passed on by reference")]
    [DataRow("Sub Bump(ByRef N As Long) ;; N = N + 1 ;; End Sub", "Dim l As Long ;; l = 5 ;; Bump (l) ;; Debug.Print l", "5", DisplayName = "a variable in parentheses is passed by value")]
    [DataRow("Sub Grow(ByRef I As Integer) ;; I = I + 30000 ;; End Sub", "Dim i As Integer ;; i = 10000 ;; Grow i ;; Debug.Print Err.Number & \" \" & i", "6 10000", DisplayName = "an assignment past the range, by reference")]
    [DataRow("Function Mix(ByVal A As Integer, ByVal B As Double) As Double ;; Mix = A + B ;; End Function", "Debug.Print Mix(2, 0.5) ;; Debug.Print Mix(40000, 1) ;; Debug.Print Err.Number", "6", DisplayName = "an argument past the range of its parameter")]
    [DataRow("Function Opt(ByVal A As Long, Optional ByVal B As Long = 3) As Long ;; Opt = A + B ;; End Function", "Debug.Print Opt(1) & \" \" & Opt(1, 2)", "4 3", DisplayName = "an optional parameter, omitted and given")]
    [DataRow("Function Quot(ByVal A As Long, ByVal B As Long) As Long ;; Quot = A \\ B ;; End Function", "Debug.Print Quot(7, 0) ;; Debug.Print Err.Number", "11", DisplayName = "an error in the callee")]
    [DataRow("Sub Loop10(ByRef N As Long) ;; Dim i As Long ;; For i = 1 To 10 ;; N = N + i ;; Next ;; End Sub", "Dim s As Long ;; Loop10 s ;; Loop10 s ;; Debug.Print s", "110", DisplayName = "a sum accumulated by reference")]
    [DataRow("Function Half2(ByVal A As Long) As Double ;; Half2 = A / 2 ;; End Function", "Debug.Print Half2(2.5) & \" \" & Half2(3.5)", "1 2", DisplayName = "an argument rounded to its parameter (boxed)")]
    [DataRow("Function Dbl(ByVal x As Variant) As Variant ;; Dbl = x * 2 ;; End Function", "Debug.Print Dbl(21) & \" \" & TypeName(Dbl(21))", "42 Integer", DisplayName = "a Variant passed by value and returned")]
    [DataRow("Sub Inc(ByRef x As Variant) ;; x = x + 1 ;; End Sub", "Dim v As Variant ;; v = 1 ;; Inc v ;; Inc v ;; Debug.Print v", "3", DisplayName = "a Variant passed by reference")]
    public async Task ACall_OnPayloads_IsWhatTheBoxedEvaluationCalls(string procedures, string body, string printed)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
            + "Private Function Twice(ByVal N As Long) As Long\r\nTwice = 2 * N\r\nEnd Function\r\n"
            + (procedures.Length == 0 ? string.Empty : string.Join("\r\n", procedures.Split(" ;; ")) + "\r\n")
            + $"Public Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nEnd Sub\r\n";

        Assert.AreEqual(printed, (await RunAsync([], program)).Last());
    }
}
