using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.6.9.5, §5.6.9.6, §5.6.9.8</strong>: a relational operator, <c>Like</c> and a logical operator with an operand declared <c>Variant</c> are
/// declared <c>Variant</c>, and what they evaluate to is a Variant's. Compared with a Variant String, <c>MinValue Or MaxValue</c> is a Variant number, which ranks
/// below it whatever its value (MS-VBAL §5.6.9.5's exception): <c>A &lt; (MinValue Or MaxValue)</c> is <c>False</c> for every <c>A</c>.
/// </summary>
/// <remarks>
/// Each row is the probe it was recorded with in Excel, in a module with no <c>Option Explicit</c>, so that <c>A</c>, <c>MinValue</c> and <c>MaxValue</c> are
/// implicit Variants, and keeps the name it was recorded with. Each runs in the interpreter and in the compiled engine, which give the same.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.9 Operators")]
public sealed class VariantLogicalRelationalResultTests
{
    private static readonly VBExecutionEngine[] Engines = [VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled];

    private static async Task<string> RunAsync(string program, VBExecutionEngine engine)
    {
        string[] output = [];
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], program, (_, run, _) =>
        {
            output = [.. run.Output.Select(line => line.Trim())];
            return Task.CompletedTask;
        }, engine: engine);

        return string.Join("/", output);
    }

    // what the program prints, in each engine: the same, which is what MS-VBA printed.
    private static async Task AssertPrintsAsync(string program, string expected)
    {
        foreach (var engine in Engines)
        {
            Assert.AreEqual(expected, await RunAsync(program, engine), $"in the {engine} engine");
        }
    }

    // the probes' module: MinValue = 0, MaxValue = 255 and A, implicit Variants unless dims declares one, and the variables the y rows compare with.
    private static string Program(string dims, string a, params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error Resume Next\r\nDim L As Long, S As String, I As Integer, o As Object\r\n"
            + (dims.Length > 0 ? dims + "\r\n" : string.Empty)
            + $"MinValue = 0\r\nMaxValue = 255\r\nA = {a}\r\n"
            + "L = 255: S = \"40\": N = -256: bT = True: bF = False: dt = DateSerial(2000, 1, 1): A2 = \"1/1/1990\": Set vo = Nothing\r\n"
            + string.Join("\r\n", body) + "\r\nEnd Sub\r\n";

    // a row of the grids: the error, the TypeName and the value of what the expression evaluates to, assigned to the Variant v.
    private static string Probe(string dims, string a, string expression)
        => Program(dims, a, $"v = {expression}", "Debug.Print Err.Number & \"|\" & TypeName(v) & \"|\" & CStr(v)");

    [TestMethod]
    [DataRow("A < (MaxValue)", "0|Boolean|False", DisplayName = "x01 A < (MaxValue)")]
    [DataRow("A < (MinValue + MaxValue)", "0|Boolean|False", DisplayName = "x02 A < (MinValue + MaxValue)")]
    [DataRow("A < (MinValue Or MaxValue)", "0|Boolean|False", DisplayName = "x03 A < (MinValue Or MaxValue)")]
    [DataRow("A < -MaxValue", "0|Boolean|False", DisplayName = "x04 A < -MaxValue")]
    [DataRow("A < CVar(MinValue Or MaxValue)", "0|Boolean|False", DisplayName = "x05 A < CVar(MinValue Or MaxValue)")]
    [DataRow("(MinValue Or MaxValue) > A", "0|Boolean|False", DisplayName = "x06 (MinValue Or MaxValue) > A")]
    [DataRow("A < MinValue + 255", "0|Boolean|False", DisplayName = "x07 A < MinValue + 255")]
    [DataRow("A < (MinValue Or 255)", "0|Boolean|False", DisplayName = "x09 A < (MinValue Or 255)")]
    [DataRow("A < Abs(MaxValue)", "0|Boolean|False", DisplayName = "x10 A < Abs(MaxValue)")]
    [DataRow("A < MaxValue * 1", "0|Boolean|False", DisplayName = "x11 A < MaxValue * 1")]
    [DataRow("A < (A < MaxValue)", "0|Boolean|False", DisplayName = "x12 A < (A < MaxValue)")]
    public async Task AVariantOperation_IsAVariant_ComparedWithAVariantString(string expression, string expected)
        => await AssertPrintsAsync(Probe(string.Empty, "\"40\"", expression), expected);

    // x08: what a Variant is assigned is held in it, an operation's result or not.
    [TestMethod]
    public async Task AStoredResult_IsAVariant()
        => await AssertPrintsAsync(Program(string.Empty, "\"40\"", "v2 = MinValue Or MaxValue", "v = A < v2",
            "Debug.Print Err.Number & \"|\" & TypeName(v) & \"|\" & CStr(v)"), "0|Boolean|False");

    // the program's own grid: A of three Strings, MinValue an implicit Variant (U) or a Long (L), MaxValue a Variant either way.
    [TestMethod]
    [DataRow("", "\"40\"", "A < (MinValue Or MaxValue)", "0|Boolean|False", DisplayName = "r03 U 40")]
    [DataRow("", "\"-5\"", "A < (MinValue Or MaxValue)", "0|Boolean|False", DisplayName = "r03 U -5")]
    [DataRow("", "\"abc\"", "A < (MinValue Or MaxValue)", "0|Boolean|False", DisplayName = "r03 U abc")]
    [DataRow("Dim MinValue As Long", "\"40\"", "A < (MinValue Or MaxValue)", "0|Boolean|False", DisplayName = "r03 L 40")]
    [DataRow("Dim MinValue As Long", "\"-5\"", "A < (MinValue Or MaxValue)", "0|Boolean|False", DisplayName = "r03 L -5")]
    [DataRow("Dim MinValue As Long", "\"abc\"", "A < (MinValue Or MaxValue)", "0|Boolean|False", DisplayName = "r03 L abc")]
    [DataRow("", "\"40\"", "(A < MinValue) Or MaxValue", "0|Integer|255", DisplayName = "r02 U 40")]
    [DataRow("Dim MinValue As Long", "\"-5\"", "A < MinValue", "0|Boolean|True", DisplayName = "r01 L -5")]
    [DataRow("Dim MinValue As Long", "\"-5\"", "(A < MinValue) Or MaxValue", "0|Integer|-1", DisplayName = "r02 L -5")]
    [DataRow("", "\"40\"", "A < MinValue Or MaxValue", "0|Integer|255", DisplayName = "r04 U 40")]
    [DataRow("", "\"40\"", "MinValue Or MaxValue", "0|Integer|255", DisplayName = "r11 U 40")]
    [DataRow("Dim MinValue As Long", "\"40\"", "MinValue Or MaxValue", "0|Long|255", DisplayName = "r11 L 40")]
    [DataRow("", "\"40\"", "False Or MaxValue", "0|Integer|255", DisplayName = "r12 U 40")]
    [DataRow("", "\"40\"", "A < 255", "0|Boolean|True", DisplayName = "r09 U 40")]
    public async Task TheAuthorsGrid_IsWhatMSVBAGives(string dims, string a, string expression, string expected)
        => await AssertPrintsAsync(Probe(dims, a, expression), expected);

    // every logical operator, every relational operator and Like, of a Variant operand, compared with A, a Variant "40": each is a Variant, ranked below
    // A; Is is a Boolean, and so is an operation of operands declared otherwise, which A is compared with by value (L, I and o are declared).
    [TestMethod]
    [DataRow("A < (MaxValue And MaxValue)", "0|Boolean|False", DisplayName = "y01 And")]
    [DataRow("A < (MinValue Xor MaxValue)", "0|Boolean|False", DisplayName = "y02 Xor")]
    [DataRow("A < (MaxValue Eqv -1)", "0|Boolean|False", DisplayName = "y03 Eqv")]
    [DataRow("A < (-1 Imp MaxValue)", "0|Boolean|False", DisplayName = "y04 Imp, the Variant on the right")]
    [DataRow("A < (Not N)", "0|Boolean|False", DisplayName = "y05 Not")]
    [DataRow("A < (0 Or MaxValue)", "0|Boolean|False", DisplayName = "y06 Or, the Variant on the right")]
    [DataRow("A > (MaxValue = MaxValue)", "0|Boolean|True", DisplayName = "y07 =")]
    [DataRow("A = (MaxValue = MaxValue)", "0|Boolean|False", DisplayName = "y08 = of =, never equal")]
    [DataRow("A > (MaxValue <> MinValue)", "0|Boolean|True", DisplayName = "y09 <>")]
    [DataRow("A > (MaxValue >= MinValue)", "0|Boolean|True", DisplayName = "y10 >=")]
    [DataRow("A > (MaxValue > MinValue)", "0|Boolean|True", DisplayName = "y11 >")]
    [DataRow("A > (MinValue <= MaxValue)", "0|Boolean|True", DisplayName = "y12 <=")]
    [DataRow("A > (A Like \"4*\")", "0|Boolean|True", DisplayName = "y13 Like")]
    [DataRow("A > (o Is Nothing)", "0|Boolean|False", DisplayName = "y14 Is, declared Object")]
    [DataRow("A > (vo Is Nothing)", "0|Boolean|False", DisplayName = "y15 Is, of a Variant")]
    [DataRow("A < (L Or 255)", "0|Boolean|True", DisplayName = "y16 Or of a Long and a literal")]
    [DataRow("A < (L Or MaxValue)", "0|Boolean|False", DisplayName = "y17 Or of a Long and a Variant")]
    [DataRow("A > (I = 0)", "0|Boolean|False", DisplayName = "y18 = of an Integer and a literal")]
    [DataRow("A > (I = MaxValue)", "0|Boolean|True", DisplayName = "y19 = of an Integer and a Variant")]
    [DataRow("S < (MinValue Or MaxValue)", "0|Boolean|False", DisplayName = "y20 a String and a Variant Or, as text")]
    [DataRow("S < (MaxValue = MaxValue)", "0|Boolean|True", DisplayName = "y21 a String and a Variant =, as text")]
    [DataRow("A < (MinValue Or MaxValue) Or MaxValue", "0|Integer|255", DisplayName = "y29 the Or of a ranked comparison")]
    public async Task EveryLogicalAndRelationalOperator_OfAVariant_IsAVariant(string expression, string expected)
        => await AssertPrintsAsync(Probe(string.Empty, "\"40\"", expression), expected);

    // a Variant Boolean and a Variant Date are ranked below a Variant String as a number is, whatever their values (MS-VBA, where MS-VBAL 5.6.9.5 says
    // "a numeric value type"): bT and bF are Variant True and False, dt a Variant Date of 1/1/2000, A2 a Variant "1/1/1990".
    [TestMethod]
    [DataRow("A < bF", "0|Boolean|False", DisplayName = "y22 A < a Variant False")]
    [DataRow("A > bT", "0|Boolean|True", DisplayName = "y23 A > a Variant True")]
    [DataRow("A = bT", "0|Boolean|False", DisplayName = "y24 A = a Variant True")]
    [DataRow("bT < A", "0|Boolean|True", DisplayName = "y25 a Variant True < A")]
    [DataRow("A > dt", "0|Boolean|True", DisplayName = "y26 A > a Variant Date")]
    [DataRow("A2 < dt", "0|Boolean|False", DisplayName = "y27 a Variant date String < a Variant Date")]
    public async Task AVariantBooleanOrDate_IsRankedBelowAVariantString(string expression, string expected)
        => await AssertPrintsAsync(Probe(string.Empty, "\"40\"", expression), expected);

    // y28: what the operations are of, which TypeName and VarType say of their values.
    [TestMethod]
    public async Task TheTypeOfTheValue_IsTheEffectiveTypes()
        => await AssertPrintsAsync(Program(string.Empty, "\"40\"",
            "Debug.Print Err.Number & \"|\" & TypeName(MinValue Or MaxValue) & \"|\" & VarType(MinValue Or MaxValue) & \"|\" & TypeName(A < MaxValue) & \"|\" & VarType(A < MaxValue) & \"|\" & VarType(Not N)"),
            "0|Integer|2|Boolean|11|2");

    // the branch a condition, a Case, a loop and IIf take on such an expression is MS-VBA's.
    [TestMethod]
    [DataRow("If A < (MinValue Or MaxValue) Then br = \"THEN\" Else br = \"ELSE\"", "ELSE", DisplayName = "b01 If")]
    [DataRow("If (MinValue Or MaxValue) > A Then br = \"THEN\" Else br = \"ELSE\"", "ELSE", DisplayName = "b02 If, the Variant on the left")]
    [DataRow("If A > (MaxValue = MaxValue) Then br = \"THEN\" Else br = \"ELSE\"", "THEN", DisplayName = "b03 If of a comparison")]
    [DataRow("Select Case A ;; Case Is < (MinValue Or MaxValue): br = \"lt\" ;; Case Else: br = \"else\" ;; End Select", "else", DisplayName = "b04 Case Is <")]
    [DataRow("Select Case A ;; Case 0 To (MinValue Or MaxValue): br = \"in\" ;; Case Else: br = \"else\" ;; End Select", "else", DisplayName = "b05 Case To")]
    [DataRow("Select Case A ;; Case (MaxValue = MaxValue): br = \"eq\" ;; Case Else: br = \"else\" ;; End Select", "else", DisplayName = "b06 Case of a comparison")]
    [DataRow("Select Case (MinValue Or MaxValue) ;; Case Is > A: br = \"gt\" ;; Case Else: br = \"else\" ;; End Select", "else", DisplayName = "b07 the selector an Or")]
    [DataRow("k = 0: Do While A < (MinValue Or MaxValue) And k < 3: k = k + 1: Loop ;; br = k", "0", DisplayName = "b08 Do While")]
    [DataRow("br = IIf(A < (MinValue Or MaxValue), \"T\", \"F\")", "F", DisplayName = "b09 IIf")]
    [DataRow("Select Case True ;; Case A < (MinValue Or MaxValue): br = \"lt\" ;; Case Else: br = \"else\" ;; End Select", "else", DisplayName = "b10 Select Case True")]
    public async Task TheBranchTaken_IsMSVBAs(string body, string expected)
        => await AssertPrintsAsync(Program(string.Empty, "\"40\"", [.. body.Split(" ;; "), "Debug.Print Err.Number & \"|\" & br"]), $"0|{expected}");

    // a For loop of a Variant counter compares it with its end, a comparison declared Variant, and runs to its end both ways.
    [TestMethod]
    public async Task AForLoopOfAVariantCounter_RunsToItsEnd()
        => await AssertPrintsAsync(Program(string.Empty, "\"40\"", "n = 0", "For k = MinValue To 3: n = n + 1: Next", "For k = 3 To MinValue Step -1: n = n + 1: Next",
            "Debug.Print Err.Number & \"|\" & n & \"|\" & k"), "0|8|-1");

    // a procedure of Variant numbers only runs compiled, its Variant operations on payloads, and gives what the interpreter gives.
    [TestMethod]
    public async Task VariantOperationsOfNumbers_RunCompiled_AsTheInterpreterRunsThem()
    {
        CompiledExecutionTests.RequirePayloads();
        const string program = "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDebug.Print Flags()\r\nEnd Sub\r\n"
            + "Private Function Flags() As Long\r\nDim MinValue, MaxValue, N, bT, r\r\nMinValue = 0: MaxValue = 255: N = -256: bT = True: r = 0\r\n"
            + "If bT < (MinValue Or MaxValue) Then r = r + 1\r\nIf (MinValue Xor MaxValue) = 255 Then r = r + 2\r\nIf (Not N) = 255 Then r = r + 4\r\n"
            + "If (MaxValue = MaxValue) = bT Then r = r + 8\r\nIf (MaxValue Eqv -1) = 255 Then r = r + 16\r\nFlags = r\r\nEnd Function\r\n";

        IReadOnlyDictionary<string, CompilationResult> compiled = new Dictionary<string, CompilationResult>();
        string[] output = [];
        using (CompiledExecution.Running(VBExecutionEngine.Compiled))
        {
            await ModuleWorkspace.InspectAsync([], program, (provider, run, _) =>
            {
                output = [.. run.Output.Select(line => line.Trim())];
                compiled = CompiledExecutionTests.ByName(provider);
                return Task.CompletedTask;
            }, engine: VBExecutionEngine.Compiled);
        }

        Assert.AreEqual("31", string.Join("/", output));
        Assert.AreEqual("31", await RunAsync(program, VBExecutionEngine.Interpreter));
        Assert.IsTrue(compiled.TryGetValue("Flags", out var flags), "Flags was not called");
        Assert.IsNotNull(flags.Body, string.Join("; ", flags.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }
}
