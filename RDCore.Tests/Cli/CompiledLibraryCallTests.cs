using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Compiled;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A conversion, or a function of the Math module, called by compiled code inside an expression, as the interpreter calls it: through the function's
/// binding, each argument held in a <c>Variant</c> of its own subtype, with the session's locale. <c>Rnd</c>, whose seed moves at each call, is called once:
/// an instruction handed to the interpreter after it is given what it returned. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledLibraryCallTests
{
    // the program run both ways, with the classes given, the session's locale lcid and its environment changed as environment says: what it printed, the
    // error it raised and where, alike, which is returned; and each procedure named compiled, with as many statements recovered, and handed to the
    // interpreter as many times, as said.
    private static async Task<string> AssertAlikeAsync(string program, (string Name, long Recoveries)[] compiled, long walkBacks = 0, int lcid = 0,
        Func<RuntimeEnvironmentProfile, RuntimeEnvironmentProfile>? environment = null, IReadOnlyList<(string Name, string Source)>? classes = null)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, lcid, environment, classes);
        var run = await RunAsync(program, VBExecutionEngine.Compiled, lcid, environment, classes);
        Assert.AreEqual(interpreted.Observed, run.Observed);

        foreach (var (name, recoveries) in compiled)
        {
            Assert.IsTrue(run.Compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(walkBacks, result.Body.WalkBacks, $"{name}'s walk-backs, the last at {result.Body.LastWalkedBackAt}");
                Assert.AreEqual(recoveries, result.Body.Recoveries,
                    $"{name}'s recoveries; stepped: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail} {reason.Location}"))}");
            }
        }

        return run.Observed;
    }

    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> RunAsync(string program, VBExecutionEngine engine, int lcid,
        Func<RuntimeEnvironmentProfile, RuntimeEnvironmentProfile>? environment, IReadOnlyList<(string Name, string Source)>? classes)
    {
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync(classes ?? [], program, (provider, run, _) =>
        {
            made = (Observed(run), CompiledExecutionTests.ByName(provider));
            return Task.CompletedTask;
        }, engine: engine, environment: lcid == 0 && environment is null ? null : profile => (environment ?? (same => same))(lcid == 0 ? profile : profile with { Lcid = lcid }));
        return made;
    }

    // each conversion of a Double, each rounded as its own type rounds (banker's rounding to an integer), and Int, Fix and Abs of it, of its type. A
    // Currency is a Double again before it is added: the row is of the calls.
    [TestMethod]
    public async Task AConversionOrAMathFunction_IsCalledByCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Converted(6)",
            "End Sub",
            "Private Function Converted(ByVal N As Long) As Double",
            "    Dim I As Long, D As Double",
            "    For I = 1 To N",
            "        D = I * 1.75 - 3.5",
            "        Converted = Converted * 3 + CLng(D) + CInt(D / 2) + Int(D) + Fix(D) + Abs(D) + CDbl(I) / 4 + CByte(I) + CSng(D) + CDbl(CCur(D / 3))",
            "    Next",
            "End Function"), [("Converted", 0)]);

    // MS-VBAL §5.6.9.3: a conversion of an operation of Doubles converts it from its 80 bits, and Int of one is computed on them: 2.5 + 2^-60 is no
    // Double, but its extended value is past 2.5, so CLng and CInt round it up to 3, where the Double it is stored as rounds to 2; and 2.5 less it is
    // below 0, so Int of that is -1, where it is 0 of the Doubles.
    [TestMethod]
    public async Task AConversionOfAnExtendedIntermediate_ConvertsItsEightyBits()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Rounded(2.5, 8.673617379884035E-19)",
            "End Sub",
            "Private Function Rounded(ByVal D As Double, ByVal E As Double) As Long",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        Rounded = Rounded * 1000 + CLng(D + E) * 100 + CInt(D + E) * 10 + Int(2.5 - (D + E))",
            "    Next",
            "End Function"), [("Rounded", 0)]);

    // MS-VBAL §6.1.2.10.1.8, §5.6.9.3: Sgn of an operation of Doubles is the sign of its 80 bits. 1E-200 * 1E-200 is 1E-400, past the least Double but
    // not the least extended value: stored as a Double it is 0, but its sign is 1, as VBA7 gives it; its negation's is -1, and the difference of two of
    // them is 0. Each sign, plus 1, is a digit: 2, 0 and 1, for each pass, read where a Variant holds it, where it is converted to a String, and where it
    // is an operand of a Long.
    [TestMethod]
    public async Task ASgnOfAnExtendedIntermediate_IsTheSignOfItsEightyBits()
        => StringAssert.Contains(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Signs(1E-200, 1E-200)",
            "    Debug.Print Held(1E-200, 1E-200)",
            "End Sub",
            "Private Function Signs(ByVal A As Double, ByVal B As Double) As Long",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        Signs = Signs * 1000 + (Sgn(A * B) + 1) * 100 + (Sgn(-A * B) + 1) * 10 + (Sgn(A * B - A * B) + 1)",
            "    Next",
            "End Function",
            "Private Function Held(ByVal A As Double, ByVal B As Double) As String",
            "    Dim V As Variant",
            "    V = Sgn(-A * B)",
            "    Held = CStr(Sgn(A * B)) & V & Sgn(A * B - A * B)",
            "End Function"), [("Signs", 0)]), "| 201201 ; 1-10 |");

    // Rnd qualified by its module and its project, and by itself, after a seed set: each draw is made once, whichever engine makes the instruction, so the
    // numbers are the interpreter's in order.
    [TestMethod]
    public async Task Rnd_QualifiedOrNot_DrawsTheInterpretersNumbersInOrder()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim X As Single",
            "    X = Rnd(-1)",
            "    Randomize 7",
            "    Debug.Print Between(1, 6); Between(1, 6); NextSingle(); Drawn(5); Int(Rnd * 1000)",
            "End Sub",
            "Public Function Between(ByVal lower As Long, ByVal upper As Long) As Long",
            "    Between = CLng(Int((upper - lower + 1) * VBA.Math.Rnd + lower))",
            "End Function",
            "Public Function NextSingle() As Single",
            "    NextSingle = VBA.Math.Rnd",
            "End Function",
            "Private Function Drawn(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Drawn = Drawn * 7 + Between(1, 6) + Int(Rnd * 10)",
            "    Next",
            "End Function"), [("Between", 0), ("NextSingle", 0), ("Drawn", 0)]);

    // a draw made, then an overflow after it in the same instruction: the interpreter is handed the instruction with what Rnd returned, raises the error
    // there, and draws nothing more. Resume Next goes on to the next pass, which draws again.
    [TestMethod]
    public async Task AnInstructionThatFailsAfterRnd_IsHandedOverWithWhatItDrew()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim X As Single",
            "    X = Rnd(-1)",
            "    Randomize 3",
            "    Debug.Print Spun(6); Int(Rnd * 1000)",
            "End Sub",
            "Private Function Spun(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    On Error Resume Next",
            "    For I = 1 To N",
            "        Spun = Int(Rnd * 10) + Spun * 1000",
            "    Next",
            "End Function"), [("Spun", 3)]);

    // an error a conversion raises (an overflow of CByte) is the instruction's, where the interpreter raises it, with the same stack trace.
    [TestMethod]
    public async Task AnErrorAConversionRaises_IsRaisedWhereTheInterpreterRaisesIt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Bytes(5)",
            "End Sub",
            "Private Function Bytes(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Bytes = Bytes + CByte(I * 100)",
            "        Debug.Print Bytes",
            "    Next",
            "End Function"), [("Bytes", 2)], walkBacks: 1);

    // Abs of the least value of a type, which the function overflows on, is the call's to decide, as the interpreter decides it: of the least Long in a
    // constant expression, that Long; of a Long variable, error 6. Compiled code hands each to the interpreter once.
    [TestMethod]
    public async Task AbsOfTheLeastValueOfAType_IsTheInterpreters()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print LeastConstant(3)",
            "    Debug.Print LeastOfALong(-2147483647)",
            "End Sub",
            "Private Function LeastConstant(ByVal N As Long) As Double",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        LeastConstant = LeastConstant * 2 + Abs(CLng(-2147483647) - 1)",
            "    Next",
            "End Function",
            "Private Function LeastOfALong(ByVal N As Long) As Long",
            "    Dim L As Long",
            "    L = N - 1",
            "    LeastOfALong = Abs(L)",
            "End Function"), [("LeastConstant", 0), ("LeastOfALong", 0)], walkBacks: 1);

    // IsEmpty, IsNull, IsNumeric and VarType of a Variant of each subtype a payload holds.
    [TestMethod]
    public async Task AnInformationFunction_IsCalledByCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Described(CInt(3)), Described(3&), Described(2.5), Described(CSng(1.5)), Described(CCur(1.5))",
            "    Debug.Print Described(#1/2/2000#), Described(True), Described(CByte(7)), Described(-1)",
            "End Sub",
            "Private Function Described(ByVal V As Variant) As Long",
            "    Dim I As Long, T As Long, E As Boolean, N As Boolean, U As Boolean",
            "    For I = 1 To 2",
            "        T = VarType(V)",
            "        E = IsEmpty(V)",
            "        N = IsNull(V)",
            "        U = IsNumeric(V)",
            "    Next",
            "    Described = T * 1000 - E * 100 - N * 10 - U",
            "End Function"), [("Described", 0)]);

    // a function of the Math module of a Double parameter, given an Integer, a Long, a Single, a Boolean and a Double let-coerced to a Double as the
    // interpreter coerces them; the error 5 one raises outside its domain (Sqr of a negative number) is raised where the interpreter raises it.
    [TestMethod]
    public async Task AFunctionOfADoubleParameter_IsGivenItsArgumentCoerced()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Computed(CInt(4), 3&, CSng(2.25), True, 0.5)",
            "    Debug.Print Computed(1, 2, 3, False, -0.25)",
            "End Sub",
            "Private Function Computed(ByVal A As Integer, ByVal B As Long, ByVal S As Single, ByVal F As Boolean, ByVal D As Double) As Double",
            "    Dim I As Long, R As Double",
            "    For I = 1 To 2",
            "        R = Sqr(A)",
            "        R = R + Sqr(B) + Sqr(S) + Exp(F) + Exp(D) + Log(A) + Atn(D) + Cos(B) + Sin(S) + Tan(D)",
            "        Computed = R",
            "    Next",
            "    Computed = Computed + Sqr(D)",
            "End Function"), [("Computed", 0)], walkBacks: 1);

    // Sgn and the parts of a date, of a Variant they return: of a Date, of a Double read as a Date, and of numbers of each sign.
    [TestMethod]
    public async Task AFunctionOfAVariantItReturns_IsCalledByCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Parts(#3/14/2015 9:26:53 PM#, -2.5), Parts(36526.75, 0), Parts(#12/31/1899#, 7)",
            "End Sub",
            "Private Function Parts(ByVal D As Date, ByVal X As Double) As Double",
            "    Dim I As Long, R As Double, V As Variant",
            "    V = X",
            "    For I = 1 To 2",
            "        R = Year(D)",
            "        R = R * 100 + Month(D)",
            "        R = R * 100 + Day(D)",
            "        R = R * 100 + Hour(D)",
            "        R = R * 100 + Minute(D)",
            "        R = R * 100 + Second(D)",
            "        R = R * 10 + Sgn(X) + Sgn(V)",
            "    Next",
            "    Parts = R",
            "End Function"), [("Parts", 0)]);

    // Round, of a Variant it returns, with its optional argument left out and given (an Integer let-coerced to the Long its parameter is); DateSerial and
    // TimeSerial, of Integer parameters given a Long, an Integer and an operation of Doubles let-coerced as the interpreter coerces them, a month or a minute
    // past its range rolling over. The error 5 Round raises for a negative number of digits is raised where the interpreter raises it: Round's typed form
    // declines it, and the interpreter runs the statement, which ends the run there: no walk-back, and no recovery that goes on compiled.
    [TestMethod]
    public async Task AFunctionOfOptionalOrTypedParameters_IsCalledByCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Made(2.5, 2), Made(3.5, 1), Made(-2.345, 0)",
            "    Debug.Print Made(1.5, -1)",
            "End Sub",
            "Private Function Made(ByVal X As Double, ByVal N As Integer) As Double",
            "    Dim I As Long, R As Double, M As Long, D As Date",
            "    M = 13",
            "    For I = 1 To 2",
            "        R = Round(X)",
            "        R = R * 1000 + Round(X * 1.111, N)",
            "        D = DateSerial(2024, M, N + 1)",
            "        R = R * 100000 + D",
            "        D = TimeSerial(M, 61, X * 10)",
            "        R = R + D",
            "    Next",
            "    Made = R",
            "End Function"), [("Made", 0)]);

    // IsEmpty, IsNull, IsNumeric and VarType of a Variant that holds Empty, Null, a String, an error, Missing or an object, which compiled code gives the
    // function as the interpreter gives it.
    [TestMethod]
    public async Task AnInformationFunctionOfAnyVariant_IsCalledByCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim V As Variant",
            "    Debug.Print Described(V), Described(Null), Described(\"12\"), Described(\"x\"), Described(CVErr(5)), Described()",
            "    Debug.Print Described(New Collection)",
            "End Sub",
            "Private Function Described(Optional ByVal V As Variant) As Long",
            "    Dim I As Long, T As Long, E As Boolean, N As Boolean, U As Boolean",
            "    For I = 1 To 2",
            "        T = VarType(V)",
            "        E = IsEmpty(V)",
            "        N = IsNull(V)",
            "        U = IsNumeric(V)",
            "    Next",
            "    Described = T * 1000 - E * 100 - N * 10 - U",
            "End Function"), [("Described", 0)]);

    // a Variant given to a function of the library is what it holds where the argument is evaluated, before a call to its right writes it (MS-VBAL
    // §5.3.1.11).
    [TestMethod]
    public async Task AVariantArgument_IsReadBeforeTheArgumentsToItsRight()
        => await AssertAlikeAsync(Program(
            "Private V As Variant",
            "Public Sub Main()",
            "    Debug.Print Chosen(), V",
            "End Sub",
            "Private Function Chosen() As Long",
            "    Dim W As Variant",
            "    W = 1",
            "    Chosen = IIf(True, W, Written(W))",
            "End Function",
            "Private Function Written(ByRef W As Variant) As Long",
            "    W = 99",
            "    V = W",
            "End Function"), []);

    // a function declared with Declare that the environment refuses is refused by compiled code as by the interpreter, with error 70: what goes straight
    // to the library is a function of the standard library, and nothing else.
    [TestMethod]
    public async Task ADeclaredFunctionThePolicyRefuses_IsRefusedByCompiledCode()
    {
        var observed = await AssertAlikeAsync(Program(
            "Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long",
            "Public Sub Main()",
            "    Debug.Print Ticks(3)",
            "End Sub",
            "Private Function Ticks(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Ticks = Ticks + CLng(I * 1.5)",
            "    Next",
            "    Ticks = Ticks + GetTickCount()",
            "End Function"), [], environment: profile => profile with { AllowDllImports = false });
        StringAssert.Contains(observed, "70");
    }

    // IsEmpty, IsNull, IsNumeric, IsObject and VarType of a Variant that holds an object, with a default member or without one: what the interpreter
    // says of it, its default member evaluated where the function evaluates it.
    [TestMethod]
    public async Task AnInformationFunctionOfAnObject_IsCalledByCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Described(New Valued), Described(New Collection)",
            "End Sub",
            "Private Function Described(ByVal V As Variant) As Long",
            "    Dim I As Long, T As Long, E As Boolean, N As Boolean, U As Boolean, O As Boolean",
            "    For I = 1 To 2",
            "        T = VarType(V)",
            "        E = IsEmpty(V)",
            "        N = IsNull(V)",
            "        U = IsNumeric(V)",
            "        O = IsObject(V)",
            "    Next",
            "    Described = T * 10000 - E * 1000 - N * 100 - U * 10 - O",
            "End Function"), [("Described", 0)],
            classes: [("Valued", ModuleWorkspace.ClassModule("Valued", "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "    Value = 100", "End Property"))]);

    // with a comma for its decimal separator (fr-FR): a number converted is the same in any locale, and a String a Variant holds ("2,5") is given to the
    // library, which reads it in the session's locale.
    [TestMethod]
    public async Task AStringConvertedInACommaDecimalLocale_IsReadInTheSessionsLocale()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Parsed(\"2,5\", 3)",
            "End Sub",
            "Private Function Parsed(ByVal V As Variant, ByVal N As Long) As Double",
            "    Dim I As Long, D As Double",
            "    For I = 1 To N",
            "        D = CDbl(V)",
            "        Parsed = Parsed * 10 + D + CLng(I * 1.5)",
            "    Next",
            "End Function"), [("Parsed", 0)], lcid: 1036);

    // MS-VBAL §6.1.2.11.1.22: Len and LenB of a String variable, a literal, Strings joined, an empty String and a null one, which compiled code gives the
    // library as a Variant holding the text; and of a Long, a Double and an Integer, the bytes each occupies.
    [TestMethod]
    public async Task LenOfAStringOrOfANumber_IsCalledByCompiledCode()
    {
        var observed = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Measured(\"abc\", 2)",
            "End Sub",
            "Private Function Measured(ByVal S As String, ByVal N As Long) As Long",
            "    Dim I As Long, L As Long, D As Double, K As Integer, E As String",
            "    For I = 1 To N",
            "        L = Len(S) + Len(\"hello\") * 10 + Len(S & \"de\") * 100 + LenB(S) * 1000 + Len(E) + Len(vbNullString)",
            "        Measured = Measured + L + Len(L) * 10000 + LenB(D) * 100000 + Len(K) * 1000000",
            "    Next",
            "End Function"), [("Measured", 0)]);
        StringAssert.Contains(observed, "| 5693106 |");
    }

    // Len of a Variant that holds a String, Empty or Null. The Len of Null is Null, which compiled code holds in no payload: the interpreter is given the
    // instruction, once.
    [TestMethod]
    public async Task LenOfAVariant_IsCalledByCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim V As Variant",
            "    Debug.Print Measured(\"abcd\"), Measured(V), Measured(Null)",
            "End Sub",
            "Private Function Measured(ByVal V As Variant) As Long",
            "    Dim I As Long, U As Boolean",
            "    For I = 1 To 2",
            "        U = IsNull(Len(V))",
            "    Next",
            "    If Not U Then Measured = Len(V)",
            "    Measured = Measured * 10 - U",
            "End Function"), [("Measured", 0)], walkBacks: 1);

    // MS-VBAL §6.1.2.3.1.12: CStr of a Long, a Double, a Boolean, a Date and a Variant, written by Debug.Print and joined to a String, in the session's
    // regional settings: a comma for the decimal separator (fr-FR) and the date in its order.
    [TestMethod]
    [DataRow(1033, DisplayName = "en-US")]
    [DataRow(1036, DisplayName = "fr-FR")]
    public async Task CStrOfAScalarOrAVariant_IsCalledByCompiledCode(int lcid)
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Written 2",
            "End Sub",
            "Private Sub Written(ByVal N As Long)",
            "    Dim I As Long, D As Double, B As Boolean, T As Date, V As Variant, S As String",
            "    T = DateSerial(2026, 11, 15)",
            "    For I = 1 To N",
            "        D = I * 1.25",
            "        B = I > 1",
            "        V = I * 0.5",
            "        S = CStr(I) & \"|\" & CStr(D) & \"|\" & CStr(B) & \"|\" & CStr(T) & \"|\" & CStr(V)",
            "        Debug.Print S; CStr(D * 2)",
            "    Next",
            "End Sub"), [("Written", 0)], lcid: lcid);

    // a String given to a conversion or an information function, inside an instruction that calls inside an expression: the instruction is the
    // interpreter's, since a String compiled code reads is not one it knows. The program runs the same: "2,5" is a number in the session's locale, and
    // "x" raises 13.
    [TestMethod]
    public async Task AStringGivenToAConversion_RunsAsTheInterpreterRunsIt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Parsed(\"2,5\"), Parsed(\"x\")",
            "End Sub",
            "Private Function Parsed(ByVal S As String) As Double",
            "    Dim I As Long",
            "    On Error Resume Next",
            "    For I = 1 To 2",
            "        Parsed = VarType(S) * 100 - IsNumeric(S) * 10",
            "        Parsed = Parsed + CDbl(S)",
            "    Next",
            "End Function"), [], lcid: 1036);

    // MS-VBAL §6.1.2.11.1.19, .23, .25, .30, .17 and .41: Left, Mid, Right, the trims and the cases of a String variable, a literal and a Variant, each in its
    // $ form, of Strings, joined into a String the procedure writes, and in its Variant form, assigned to a String; Mid with no length.
    [TestMethod]
    public async Task AStringFunction_IsCalledByCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Cut(\"  RD-VBA  \", \"Ducky\", 2)",
            "End Sub",
            "Private Function Cut(ByVal P As String, ByVal V As Variant, ByVal N As Long) As String",
            "    Dim I As Long, S As String, W As String, R As String",
            "    S = Trim$(P)",
            "    For I = 1 To N",
            "        R = R & Left$(S, I) & \"|\" & Mid$(S, 2, 3) & \"|\" & Right$(S, I) & \"|\" & Mid$(S, 4) & \"|\" & Left$(P & S, 3)",
            "        R = R & \"[\" & LTrim$(P) & \"][\" & RTrim$(P) & \"]\" & UCase$(S) & LCase$(S) & UCase$(P) & LCase$(\"ABC\") & \";\"",
            "        W = Left(V, 2)",
            "        R = R & W",
            "        W = Mid(S, 4)",
            "        R = R & W",
            "        W = RTrim(P)",
            "        R = R & W",
            "        W = Trim(P)",
            "        R = R & W",
            "        W = LCase(S)",
            "        R = R & W",
            "        W = UCase(V)",
            "        R = R & W",
            "    Next",
            "    Cut = R",
            "End Function"), [("Cut", 0)]);

    // a Variant function of Null returns Null, which no String holds: the interpreter is given the call, and raises 94 (Invalid use of Null) as it assigns
    // it. Resume Next goes on to the next pass.
    [TestMethod]
    public async Task AStringFunctionOfNull_IsTheInterpretersCall()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Cut(Null), Cut(\"abc\")",
            "End Sub",
            "Private Function Cut(ByVal V As Variant) As String",
            "    Dim I As Long, S As String",
            "    On Error Resume Next",
            "    For I = 1 To 2",
            "        S = Left(V, 2)",
            "        Cut = Cut & S & Err.Number",
            "    Next",
            "End Function"), []);

    // MS-VBAL §6.1.2.11.1.8 and §6.1.2.7.1.12: Format$ and Format of a Double, with a format and with none, in the session's regional settings, and
    // TypeName of a Double, a String, Empty, Null and a Variant holding an object.
    [TestMethod]
    [DataRow(1033, DisplayName = "en-US")]
    [DataRow(1036, DisplayName = "fr-FR")]
    public async Task FormatOrTypeNameOfAValue_IsCalledByCompiledCode(int lcid)
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Written(2, Null, New Collection)",
            "End Sub",
            "Private Function Written(ByVal N As Long, ByVal U As Variant, ByVal O As Variant) As String",
            "    Dim I As Long, D As Double, V As Variant, E As Variant, W As String, R As String",
            "    For I = 1 To N",
            "        D = I * 1234.5678",
            "        V = D",
            "        R = R & Format$(D, \"0.00\") & \"|\" & Format$(D) & \"|\" & Format$(V, \"#,##0.0\") & \"|\"",
            "        W = Format(D)",
            "        R = R & W & TypeName(V) & TypeName(\"x\") & TypeName(E) & TypeName(U) & TypeName(O) & \";\"",
            "    Next",
            "    Written = R",
            "End Function"), [("Written", 0)], lcid: lcid);

    // MS-VBAL §6.1.2.11.1.14 and §6.1.2.11.1.36: InStr from the start and from a position, found and not, and StrComp, each given no Compare argument,
    // which is then the module's Option Compare (Binary here), or vbTextCompare. Null given to InStr returns Null, the interpreter's call.
    [TestMethod]
    public async Task InStrOrStrComp_IsCalledByCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Found(\"rd-VBA, rd-vba\", \"VBA\", 2)",
            "End Sub",
            "Private Function Found(ByVal S As String, ByVal V As Variant, ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Found = Found * 10 + InStr(S, \"V\") + InStr(4, S, \"vba\") * 100 + InStr(S, \"x\") + InStr(S, V) * 1000",
            "        Found = Found + StrComp(S, \"rd-vba, rd-vba\") * 10000 + StrComp(S, \"RD-VBA, RD-VBA\", vbTextCompare) * 100000 + StrComp(\"a\", S)",
            "    Next",
            "End Function"), [("Found", 0)]);

    // MS-VBAL §6.1.2.11: a Compare argument left out is the Option Compare of the calling module: Text here, so "b" is found in "ABC", and "abc" and "ABC"
    // compare equal.
    [TestMethod]
    public async Task InStrOrStrComp_WithNoCompare_ComparesAsTheModuleSays()
        => await AssertAlikeAsync(Program(
            "Option Compare Text",
            "Public Sub Main()",
            "    Debug.Print Found(\"ABC\", 2)",
            "End Sub",
            "Private Function Found(ByVal S As String, ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Found = Found * 10 + InStr(S, \"b\") + StrComp(S, \"abc\") * 100 + StrComp(S, \"abd\", vbBinaryCompare) * 1000",
            "    Next",
            "End Function"), [("Found", 0)]);

    // MS-VBAL §6.1.2.3.1.21, §6.1.2.11.1.1 and §6.1.2.11.1.3: Val of a String variable, a literal and Strings joined, which reads a period as the decimal
    // separator in any locale, and Asc and AscW of the first character. Asc of an empty String raises 5 at the statement, the interpreter's.
    [TestMethod]
    [DataRow(1033, DisplayName = "en-US")]
    [DataRow(1036, DisplayName = "fr-FR")]
    public async Task ValOrAscOfAString_IsCalledByCompiledCode(int lcid)
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Read(\"12.5kg\", 2)",
            "    Debug.Print Asc(Blank())",
            "End Sub",
            "Private Function Blank() As String",
            "End Function",
            "Private Function Read(ByVal S As String, ByVal N As Long) As Double",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Read = Read * 10 + Val(S) + Val(\"&H1F\") + Val(S & \"3\") + Asc(S) + AscW(\"é\") / 1000",
            "    Next",
            "End Function"), [("Read", 0)], lcid: lcid);

    // functions of the standard library that keep nothing, side by side in an expression and one given what another returns as a String, call nothing of the
    // workspace: the statement is compiled as one that calls nothing inside it.
    [TestMethod]
    public async Task LibraryCallsSideBySide_AreCalledByCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed(12)",
            "End Sub",
            "Private Function Summed(ByVal N As Long) As Long",
            "    Dim I As Long, L As Long",
            "    For I = 1 To N",
            "        L = L + Len(CStr(I)) + CLng(I) + Len(Left$(CStr(I * 11), 1)) * 100 + CLng(Val(Mid$(CStr(I), 1, 1))) * 1000",
            "    Next",
            "    Summed = L",
            "End Function"), [("Summed", 0)]);

    // MS-VBAL §6.1.2.4.2.2 to §6.1.2.4.2.5: Now, Date, Time and Timer, named with no arguments, qualified or not, each a reading of the session's clock:
    // what the procedure writes is of their types and of how they relate, which is the same on each run.
    [TestMethod]
    public async Task AClockReading_IsCalledByCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Read(2)",
            "End Sub",
            "Private Function Read(ByVal N As Long) As Long",
            "    Dim I As Long, T As Date, D As Date, H As Date, S As Single",
            "    For I = 1 To N",
            "        T = Now",
            "        D = Date",
            "        H = VBA.DateTime.Time",
            "        S = Timer",
            "        Read = Read * 1000 - (D <= T) - (H < 1) * 2 - (CDbl(D) = Int(CDbl(D))) * 4 - (S >= 0) * 8 - (S < 86401) * 16 + VarType(Now) * 32 + VarType(Date) * 64",
            "    Next",
            "End Function"), [("Read", 0)]);

    // MS-VBAL §6.1.2.4.1.1: DateAdd of days and months to a Date, which the procedure adds up as Dates; an interval it does not know raises 5 at the
    // statement, the interpreter's call.
    [TestMethod]
    public async Task DateAdd_IsCalledByCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Format$(Added(3), \"yyyy-mm-dd\")",
            "    Debug.Print DateAdd(\"zz\", 1, Added(1))",
            "End Sub",
            "Private Function Added(ByVal N As Long) As Date",
            "    Dim I As Long, T As Date",
            "    T = DateSerial(2026, 1, 31)",
            "    For I = 1 To N",
            "        T = DateAdd(\"d\", I, T)",
            "        T = DateAdd(\"m\", 1, T)",
            "    Next",
            "    Added = T",
            "End Function"), [("Added", 0)]);
}
