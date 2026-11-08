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
/// implicit Variants: <c>oracle\s81-or-precedence</c> (rows r and x) and <c>oracle\s81-varops</c> (rows y and b). Each runs in the interpreter and in the
/// compiled engine, which give the same.
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

    // the author's grid: A of three Strings, MinValue an implicit Variant (U) or a Long (L), MaxValue a Variant either way.
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
}
