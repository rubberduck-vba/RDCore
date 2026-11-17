using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A function of the standard library called by compiled code in its typed form, given arguments of its types, and a conversion of a whole number, which is
/// the number let-coerced: neither is a call of the library's binding. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledIntrinsicTests
{
    // the program run both ways, in the session's locale lcid: what it printed, the error it raised and where, alike; and each procedure named compiled,
    // with as many statements recovered as said, and no call of the library's binding.
    internal static async Task AssertAlikeAsync(string program, (string Name, long Recoveries)[] compiled, int lcid = 0)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, lcid);
        var run = await RunAsync(program, VBExecutionEngine.Compiled, lcid);
        Assert.AreEqual(interpreted.Observed, run.Observed);

        foreach (var (name, recoveries) in compiled)
        {
            Assert.IsTrue(run.Compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            Assert.AreEqual(0, result.Body.Libraries.Count,
                $"{name}'s calls of the library: {string.Join(", ", result.Body.Libraries.Select(library => library.Function.Name))}");
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(recoveries, result.Body.Recoveries,
                    $"{name}'s recoveries; stepped: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail} {reason.Location}"))}");
            }
        }
    }

    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> RunAsync(
        string program, VBExecutionEngine engine, int lcid)
    {
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], program, (provider, run, _) =>
        {
            made = (Observed(run), CompiledExecutionTests.ByName(provider));
            return Task.CompletedTask;
        }, engine: engine, environment: lcid == 0 ? null : profile => profile with { Lcid = lcid });
        return made;
    }

    // MS-VBAL §6.1.2.11.1.22: Len and LenB of a String variable, a parameter, a literal, Strings joined and vbNullString, added up as Longs; and Len returned
    // as a Variant, which holds a Long.
    [TestMethod]
    public async Task LenOfAString_IsItsTypedForm()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Measured(\"RD-VBA\", 3)",
            "    Debug.Print Held(\"abc\"), VarType(Held(\"abc\"))",
            "End Sub",
            "Private Function Measured(ByVal P As String, ByVal N As Long) As Long",
            "    Dim I As Long, S As String, L As Long",
            "    For I = 1 To N",
            "        S = S & P",
            "        L = L + Len(S) + LenB(S) * 100 + Len(P & S) * 10000 + Len(\"\") + LenB(vbNullString)",
            "    Next",
            "    Measured = L",
            "End Function",
            "Private Function Held(ByVal S As String) As Variant",
            "    Held = Len(S)",
            "End Function"), [("Measured", 0), ("Held", 0)]);

    // MS-VBAL §6.1.2.3.1: a conversion to a numeric type of a whole number of each width, which is the number let-coerced to the type. CInt of a Long out of
    // an Integer's range raises 6 (Overflow): the interpreter's, given the statement, which Resume Next goes on after.
    [TestMethod]
    public async Task AConversionOfAWholeNumber_IsItsLetCoercion()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Converted(4), Cents(4)",
            "End Sub",
            "Private Function Converted(ByVal N As Long) As Double",
            "    Dim I As Long, B As Byte, K As Integer, D As Double",
            "    On Error Resume Next",
            "    For I = 1 To N",
            "        B = I",
            "        K = CInt(I * 3)",
            "        D = D + CLng(I) + CInt(B) * 2 + CDbl(I) / 4 + CByte(K) + CLngLng(I) + CSng(I) / 8",
            "    Next",
            "    K = CInt(N * 10000)",
            "    Converted = D + K + Err.Number",
            "End Function",
            "Private Function Cents(ByVal N As Long) As Currency",
            "    Dim I As Long, C As Currency",
            "    For I = 1 To N",
            "        C = C + CCur(I)",
            "    Next",
            "    Cents = C",
            "End Function"), [("Converted", 1), ("Cents", 0)]);

    // MS-VBAL §6.1.2.3.1.12: CStr of a whole number is its digits, whatever the regional settings, as the let-coercion writes it; and the case the
    // benchmark makes, Len of CStr of a Long added to CLng of it.
    [TestMethod]
    [DataRow(1033, DisplayName = "en-US")]
    [DataRow(1036, DisplayName = "fr-FR")]
    public async Task CStrOfAWholeNumber_IsItsLetCoercion(int lcid)
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Written(12)",
            "    Debug.Print Summed(12)",
            "End Sub",
            "Private Function Written(ByVal N As Long) As String",
            "    Dim I As Long, B As Byte, R As String",
            "    For I = 1 To N",
            "        B = I * 20",
            "        R = R & CStr(I) & \"|\" & CStr(-I * 1000) & \"|\" & CStr(B) & \";\"",
            "    Next",
            "    Written = R",
            "End Function",
            "Private Function Summed(ByVal N As Long) As Long",
            "    Dim I As Long, L As Long",
            "    For I = 1 To N",
            "        L = L + Len(CStr(I)) + CLng(I)",
            "    Next",
            "    Summed = L",
            "End Function"), [("Written", 0), ("Summed", 0)], lcid);

    // MS-VBAL §6.1.2.11.1.36: StrComp of Strings with Compare left out, which is the module's Option Compare, and given as vbBinaryCompare, vbTextCompare or
    // a locale's identifier, which collates by that locale, each held in an Integer; a Compare MS-VBA refuses (vbDatabaseCompare, 2) raises 5: the
    // interpreter's, given the statement, which Resume Next goes on after.
    [TestMethod]
    [DataRow("Binary", 1033, DisplayName = "Option Compare Binary, en-US")]
    [DataRow("Text", 1033, DisplayName = "Option Compare Text, en-US")]
    [DataRow("Binary", 1036, DisplayName = "Option Compare Binary, fr-FR")]
    [DataRow("Text", 1036, DisplayName = "Option Compare Text, fr-FR")]
    public async Task StrCompOfStrings_IsItsTypedForm(string compare, int lcid)
        => await AssertAlikeAsync(Program(
            $"Option Compare {compare}",
            "Public Sub Main()",
            "    Debug.Print Ordered(2, \"abc\", \"ABC\", ChrW(233))",
            "End Sub",
            "Private Function Ordered(ByVal N As Long, ByVal A As String, ByVal B As String, ByVal E As String) As Long",
            "    Dim I As Long, L As Long, K As Integer",
            "    On Error Resume Next",
            "    For I = 1 To N",
            "        K = StrComp(A, B): L = L * 3 + K + 1",
            "        K = StrComp(A, B, vbBinaryCompare): L = L * 3 + K + 1",
            "        K = StrComp(A, B & \"d\", vbTextCompare): L = L * 3 + K + 1",
            "        K = StrComp(E, \"f\", 1033): L = L * 3 + K + 1",
            "        K = StrComp(E, \"f\"): L = L * 3 + K + 1",
            "        K = StrComp(vbNullString, \"\"): L = L * 3 + K + 1",
            "    Next",
            "    K = 7",
            "    K = StrComp(A, B, 2)",
            "    Ordered = L * 10 + K + Err.Number",
            "End Function"), [("Ordered", 1)], lcid);

    // MS-VBAL §5.5.1.2.4: IsNumeric of a String is whether it Let-coerces to a Double, by the session's regional settings: grouped, signed, in parentheses,
    // with the currency symbol, an exponent, a radix literal, too large for a Double, and not a number.
    [TestMethod]
    [DataRow(1033, DisplayName = "en-US")]
    [DataRow(1036, DisplayName = "fr-FR")]
    public async Task IsNumericOfAString_IsItsTypedForm(int lcid)
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim L As Long",
            "    L = Tally(L, \"12\"): L = Tally(L, \" 12 \"): L = Tally(L, \"1,000.5\"): L = Tally(L, \"1.000,5\")",
            "    L = Tally(L, \"1,5\"): L = Tally(L, \"1.5\"): L = Tally(L, \"$1\"): L = Tally(L, \"1-\")",
            "    L = Tally(L, \"(1)\"): L = Tally(L, \"(-1)\"): L = Tally(L, \"1e3\"): L = Tally(L, \"1e400\")",
            "    L = Tally(L, \"&HFF\"): L = Tally(L, \"&H\"): L = Tally(L, \"abc\"): L = Tally(L, \"\")",
            "    L = Tally(L, ChrW(8364) & \"1\"): L = Tally(L, \"1\" & ChrW(160) & \"000\"): L = Tally(L, String$(310, \"9\"))",
            "    L = Tally(L, vbNullString)",
            "    Debug.Print L",
            "End Sub",
            "Private Function Tally(ByVal L As Long, ByVal S As String) As Long",
            "    If IsNumeric(S) Then",
            "        Tally = L * 2 + 1",
            "    Else",
            "        Tally = L * 2",
            "    End If",
            "End Function"), [("Tally", 0)], lcid);

    // MS-VBAL §6.1.2.3.1 and §5.5.1.2.1: a conversion of a Double to an integral type is the Double let-coerced, rounded half to even (2.5 is 2, 3.5 is 4,
    // -2.5 is -2). One out of the type's range raises 6 (Overflow): the interpreter's, given the statement.
    [TestMethod]
    public async Task AConversionOfADouble_IsItsLetCoercion()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Rounded(12), Overflowed()",
            "End Sub",
            "Private Function Rounded(ByVal N As Long) As Double",
            "    Dim I As Long, D As Double, R As Double",
            "    For I = -N To N",
            "        D = I / 4 + 0.5",
            "        R = R + CLng(D) + CInt(D) * 10 + CLngLng(D) * 100",
            "        If D >= 0 Then R = R + CByte(D) * 1000",
            "    Next",
            "    Rounded = R",
            "End Function",
            "Private Function Overflowed() As Long",
            "    Dim D As Double, K As Integer",
            "    On Error Resume Next",
            "    D = 40000.5",
            "    K = CInt(D)",
            "    Overflowed = Err.Number + K",
            "End Function"), [("Rounded", 0), ("Overflowed", 1)]);

    // MS-VBAL §6.1.2.10.1.1: Abs of an Integer, a Long and a Double is of the same type. The smallest Long and the smallest Integer have no absolute value of
    // their types and raise 6 (Overflow): the interpreter's, given the statement, which Resume Next goes on after.
    [TestMethod]
    public async Task AbsOfANumber_IsItsTypedForm()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Absolute(5), Least(), LeastInteger()",
            "End Sub",
            "Private Function Absolute(ByVal N As Long) As Double",
            "    Dim I As Long, K As Integer, D As Double, R As Double",
            "    For I = -N To N",
            "        K = I * 100",
            "        D = I / 4",
            "        R = R + Abs(-I) + Abs(K) * 10 + Abs(D) * 1000 + Abs(I - 2) * 10000",
            "    Next",
            "    Absolute = R",
            "End Function",
            "Private Function Least() As Long",
            "    Dim L As Long",
            "    On Error Resume Next",
            "    L = -2147483647",
            "    L = Abs(L - 1)",
            "    Least = Err.Number + L",
            "End Function",
            "Private Function LeastInteger() As Long",
            "    Dim K As Integer",
            "    On Error Resume Next",
            "    K = -32767",
            "    K = Abs(K - 1)",
            "    LeastInteger = Err.Number + K",
            "End Function"), [("Absolute", 0), ("Least", 1), ("LeastInteger", 1)]);

    // MS-VBAL §6.1.2.3.1.18 and §6.1.2.3.1.16: Int and Fix of a Double, which agree but for a negative fraction.
    [TestMethod]
    public async Task IntAndFixOfADouble_AreTheirTypedForms()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Whole(6)",
            "End Sub",
            "Private Function Whole(ByVal N As Long) As Double",
            "    Dim I As Long, D As Double, R As Double",
            "    For I = -N To N",
            "        D = I / 4 + 0.1",
            "        R = R + Int(D) * 1000 + Fix(D) + Int(-D) * 10 + Fix(-D) * 100",
            "    Next",
            "    Whole = R",
            "End Function"), [("Whole", 0)]);

    // MS-VBAL §6.1.2.3.1.18 and §6.1.2.3.1.16: Int and Fix of a Date are CDate(Int(CDbl(Number))) and CDate(Fix(CDbl(Number))), a Date, which a Variant
    // holds as one (VarType 7), as Int and Fix of a Variant that holds a Date are. Before 1899-12-30 a Date's serial is negative, and Int goes a day down
    // where Fix does not. Int of a time on the first day of the Date range is below the range, which raises 6 (Overflow): the interpreter's.
    [TestMethod]
    public async Task IntAndFixOfADate_AreTheirTypedForms()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim D As Date, V As Variant",
            "    D = -1.25",
            "    V = D",
            "    Debug.Print Days(6), First()",
            "    Debug.Print VarType(Floor(D)), VarType(Truncation(D)), VarType(Int(V)), VarType(Fix(V)), CDbl(Floor(D)), CDbl(Truncation(D))",
            "End Sub",
            "Private Function Days(ByVal N As Long) As Double",
            "    Dim I As Long, D As Date, F As Double, R As Double",
            "    For I = -N To N",
            "        D = I / 4 + 0.1",
            "        F = Int(D)",
            "        R = R + F * 1000",
            "        F = Fix(D)",
            "        R = R + F",
            "    Next",
            "    Days = R",
            "End Function",
            "Private Function Floor(ByVal D As Date) As Variant",
            "    Floor = Int(D)",
            "End Function",
            "Private Function Truncation(ByVal D As Date) As Variant",
            "    Truncation = Fix(D)",
            "End Function",
            "Private Function First() As Double",
            "    Dim D As Date, F As Double",
            "    On Error Resume Next",
            "    D = -657434.5",
            "    F = Fix(D)",
            "    D = Int(D)",
            "    First = F * 10 + Err.Number",
            "End Function"), [("Days", 0), ("Floor", 0), ("Truncation", 0), ("First", 1)]);

    // MS-VBAL §6.1.2.10.1.10: Sqr of a Double, and of a Long or an Integer let-coerced to Double, as Sqr declares its parameter. A negative number has no
    // square root and raises 5 (Invalid procedure call or argument): the interpreter's.
    [TestMethod]
    public async Task SqrOfANumber_IsItsTypedForm()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Roots(8), Negative()",
            "End Sub",
            "Private Function Roots(ByVal N As Long) As Double",
            "    Dim I As Long, K As Integer, D As Double, R As Double",
            "    For I = 0 To N",
            "        K = I * 3",
            "        D = I * 2.25",
            "        R = R + Sqr(I) + Sqr(K) * 10 + Sqr(D) * 100",
            "    Next",
            "    Roots = R",
            "End Function",
            "Private Function Negative() As Long",
            "    Dim D As Double",
            "    On Error Resume Next",
            "    D = Sqr(-1)",
            "    Negative = Err.Number",
            "End Function"), [("Roots", 0), ("Negative", 1)]);

    // MS-VBAL §6.1.2.10.1.7: Round of a Double to a number of digits, given as an Integer the call let-coerces to Long, and to none; a tie goes to the even
    // number as the scaled Double is (2.675 is a little under it). Round of a quotient or a product is of its intermediate stored as a Double. A negative
    // number of digits raises 5: the interpreter's.
    [TestMethod]
    [DataRow(1033, DisplayName = "en-US")]
    [DataRow(1036, DisplayName = "fr-FR")]
    public async Task RoundOfADouble_IsItsTypedForm(int lcid)
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Rounded(8), FewerThanNone()",
            "End Sub",
            "Private Function Rounded(ByVal N As Long) As Double",
            "    Dim I As Long, D As Double, H As Double, R As Double",
            "    For I = -N To N",
            "        D = I / 8 + 0.005",
            "        H = I / 2",
            "        R = R + Round(D, 2) * 1000 + Round(D) + Round(H) * 10 + Round(I / 2) * 100 + Round(D * 3, 1) * 10000",
            "    Next",
            "    Rounded = R + Round(2.675, 2) + Round(2.5) + Round(3.5) * 10",
            "End Function",
            "Private Function FewerThanNone() As Long",
            "    Dim D As Double",
            "    On Error Resume Next",
            "    D = Round(1.5, -1)",
            "    FewerThanNone = Err.Number",
            "End Function"), [("Rounded", 0), ("FewerThanNone", 1)], lcid);

    // MS-VBAL §6.1.2.11.1.20, .26 and .31: Mid$, Left$ and Right$ of a String, a Start and a Length given as a Long or as an Integer the call let-coerces,
    // past the end too; Mid and Left, which return a Variant, assigned to a String; and vbNullString. A Start below 1 and a negative Length raise 5: the
    // interpreter's.
    [TestMethod]
    public async Task MidLeftAndRightOfAString_AreTheirTypedForms()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Cut(\"RD-VBA platform\", 5)",
            "    Debug.Print Cut(\"\", 2)",
            "    Debug.Print Wrong(\"abc\")",
            "End Sub",
            "Private Function Cut(ByVal P As String, ByVal N As Long) As String",
            "    Dim I As Long, K As Integer, R As String, S As String",
            "    For I = 1 To N",
            "        K = I",
            "        R = R & Mid$(P, I, 3) & \"|\" & Mid$(P, K) & \"|\" & Left$(P, I) & \"|\" & Right$(P, K * 2) & \"|\" & Mid$(P, 2, K) & \";\"",
            "        S = Mid(P, I, 2)",
            "        R = R & S",
            "        S = Left(P, K)",
            "        R = R & S & Right$(vbNullString, 1) & Left$(P, 0)",
            "    Next",
            "    Cut = R & Mid$(P, 100) & Mid$(P, 100, 2) & Left$(P, 100) & Right$(P, 100)",
            "End Function",
            "Private Function Wrong(ByVal P As String) As Long",
            "    Dim S As String, E As Long",
            "    On Error Resume Next",
            "    S = Mid$(P, 0)",
            "    E = Err.Number",
            "    S = Left$(P, -1)",
            "    Wrong = E * 100 + Err.Number",
            "End Function"), [("Cut", 0), ("Wrong", 2)]);

    // MS-VBAL §6.1.2.11.1.24: Trim$, LTrim$ and RTrim$ of a String remove spaces only, a tab staying; Trim, which returns a Variant, assigned to a String;
    // and the trimmed text given to Len and to Left$.
    [TestMethod]
    public async Task TrimOfAString_IsItsTypedForm()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Trimmed(\"  RD VBA \" & vbTab & \" \", 3)",
            "End Sub",
            "Private Function Trimmed(ByVal P As String, ByVal N As Long) As String",
            "    Dim I As Long, L As Long, R As String, S As String",
            "    For I = 1 To N",
            "        R = R & \"[\" & Trim$(P) & \"][\" & LTrim$(P) & \"][\" & RTrim$(P) & \"]\" & Left$(Trim$(P), I)",
            "        L = L + Len(Trim$(P)) + Len(LTrim$(vbNullString))",
            "        S = Trim(P)",
            "    Next",
            "    Trimmed = R & S & L",
            "End Function"), [("Trimmed", 0)]);

    // MS-VBAL §6.1.2.11.1.14: InStr of two Strings, from a start given as a Long or as an Integer the call let-coerces, in a module that compares Binary: a
    // zero-length pattern is found at the start, past the end too (InStr(9, "abc", "") is 9), and nothing is found in a zero-length String. A start below 1
    // raises 5: the interpreter's.
    [TestMethod]
    public async Task InStrOfTwoStrings_IsItsTypedForm()
        => await AssertAlikeAsync(Program(
            "Option Compare Binary",
            "Public Sub Main()",
            "    Debug.Print Found(\"a,b,,cA,a\", 9), Wrong(\"abc\")",
            "End Sub",
            "Private Function Found(ByVal P As String, ByVal N As Long) As Long",
            "    Dim I As Long, K As Integer, L As Long",
            "    For I = 1 To N",
            "        K = I",
            "        L = L * 3 + InStr(I, P, \",\") + InStr(K, P, \"a\") * 10 + InStr(P, \"A\") * 100 + InStr(I, \"abc\", \"\") * 1000",
            "    Next",
            "    Found = L + InStr(\"\", \"a\") + InStr(\"\", \"\") + InStr(P, vbNullString)",
            "End Function",
            "Private Function Wrong(ByVal P As String) As Long",
            "    Dim L As Long",
            "    On Error Resume Next",
            "    L = InStr(0, P, \"a\")",
            "    Wrong = Err.Number",
            "End Function"), [("Found", 0), ("Wrong", 1)]);

    // MS-VBAL §5.2.1.1: in a module that compares Text, InStr with its comparison left out is the library's, which finds "b" in "ABC".
    [TestMethod]
    public async Task InStrUnderOptionCompareText_IsTheLibrarys()
        => await AssertAlikeAsync(Program(
            "Option Compare Text",
            "Public Sub Main()",
            "    Debug.Print Found(\"ABC\")",
            "End Sub",
            "Private Function Found(ByVal P As String) As Long",
            "    Found = InStr(P, \"b\") + InStr(2, P, \"c\") * 10",
            "End Function"), []);

    // MS-VBAL §6.1.2.11.1.1 and .3: Asc of a String that begins with an ASCII character, and AscW of any, a code from 32,768 on being negative. Asc of a
    // character that is not ASCII is the code page's: the interpreter's, given the statement. A zero-length String raises 5.
    [TestMethod]
    public async Task AscOfAString_IsItsTypedForm()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Codes(\"RD\", 3), Other(\"é退\"), Wrong()",
            "End Sub",
            "Private Function Codes(ByVal P As String, ByVal N As Long) As Long",
            "    Dim I As Long, L As Long",
            "    For I = 1 To N",
            "        L = L + Asc(P) + AscW(P) * 1000& + Asc(Mid$(P, 2))",
            "    Next",
            "    Codes = L",
            "End Function",
            "Private Function Other(ByVal P As String) As Long",
            "    Dim L As Long",
            "    L = AscW(P) + AscW(Mid$(P, 2))",
            "    Other = L + Asc(P)",
            "End Function",
            "Private Function Wrong() As Long",
            "    Dim L As Long",
            "    On Error Resume Next",
            "    L = AscW(\"\")",
            "    Wrong = Err.Number",
            "End Function"), [("Codes", 0), ("Other", 1), ("Wrong", 1)]);

    // MS-VBAL §6.1.2.3.1.21: Val of a String reads the number it begins with, with "." as its decimal separator whatever the regional settings, skipping
    // spaces, tabs and line feeds, and a radix prefix; a String that begins with no number is 0. A number that does not fit raises 6: the interpreter's.
    [TestMethod]
    [DataRow(1033, DisplayName = "en-US")]
    [DataRow(1036, DisplayName = "fr-FR")]
    public async Task ValOfAString_IsItsTypedForm(int lcid)
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Read(\" 1 2.5e1x\", 3), Wrong()",
            "End Sub",
            "Private Function Read(ByVal P As String, ByVal N As Long) As Double",
            "    Dim I As Long, D As Double",
            "    For I = 1 To N",
            "        D = D + Val(P) + Val(\"&H10\") * 10 + Val(\"&O17\") + Val(\"abc\") + Val(\"\") + Val(Left$(P, I + 1))",
            "    Next",
            "    Read = D",
            "End Function",
            "Private Function Wrong() As Long",
            "    Dim D As Double",
            "    On Error Resume Next",
            "    D = Val(\"&H1FFFFFFFFFFFFFFFF\")",
            "    Wrong = Err.Number",
            "End Function"), [("Read", 0), ("Wrong", 1)], lcid);

    // MS-VBAL §6.1.2.11.1.4: Chr$ of an ASCII code, which every code page reads the same, and of one from 128, which is the code page's.
    [TestMethod]
    public async Task ChrOfACode_IsTheCharacterItNames()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Made(4)",
            "End Sub",
            "Private Function Made(ByVal N As Long) As String",
            "    Dim I As Long, R As String",
            "    For I = 0 To N",
            "        R = R & Chr$(64 + I) & Chr$(I * 31) & Chr$(196 + I)",
            "    Next",
            "    Made = R",
            "End Function"), [("Made", 0)]);
}
