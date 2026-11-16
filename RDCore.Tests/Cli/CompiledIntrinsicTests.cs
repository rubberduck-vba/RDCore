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
    private static async Task AssertAlikeAsync(string program, (string Name, long Recoveries)[] compiled, int lcid = 0)
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
}
