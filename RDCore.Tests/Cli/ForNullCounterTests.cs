using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.4.2.3, §5.6.9.5, §5.5.1.2: a <c>For</c> loop whose <c>Variant</c> counter holds <c>Null</c> when its <c>Next</c> compares it with the end. The
/// comparison is <c>Null</c>, let-coerced to <c>Boolean</c> as a condition is, which raises error 94 at the <c>Next</c>, the counter assigned <c>Null</c>; under
/// <c>On Error Resume Next</c> execution goes on after the loop. MS-VBA, recorded in Excel. Each program runs in the interpreter and
/// in compiled code alike.
/// </summary>
[TestClass]
public sealed class ForNullCounterTests
{
    // the loop, its lines numbered for Erl: each pass appends the counter, and the second sets it to Null. n guards the loop from running away.
    private static string[] Loop(string header) =>
    [
        $"10 For k = {header}",
        "20 n = n + 1: If n > 6 Then Exit For",
        "30 acc = acc & k & \"|\"",
        "40 If k = 2 Then k = Null",
        "50 Next k",
        "60 acc = acc & \"after|\"",
    ];

    private static string Probe(string handling, string header, params string[] handler) => Program(
    [
        "Public Sub Main()",
        "    Debug.Print Probe()",
        "End Sub",
        "Private Function Probe() As String",
        "    Dim k, n As Long, acc As String",
        $"    {handling}",
        .. Loop(header),
        "    acc = acc & \"E\" & Err.Number & \"|\" & TypeName(k) & \"|\" & n",
        "    GoTo X",
        "H:",
        .. handler,
        "X:",
        "    Probe = acc",
        "End Function",
    ]);

    private const string Caught = "    acc = acc & \"E\" & Err.Number & \"@\" & Erl & \"|\" & TypeName(k) & \"|\"";

    [TestMethod]
    [DataRow("1 To 3", "1|2|E94@50|Null|", DisplayName = "counting up")]
    [DataRow("3 To 1 Step -1", "3|2|E94@50|Null|", DisplayName = "counting down")]
    public async Task ANullCounter_IsError94AtTheNext_WhichTheHandlerCatches(string header, string expected)
        => await AssertRunsAsync(Probe("On Error GoTo H", header, Caught), $"| {expected} |");

    [TestMethod]
    [DataRow("1 To 3", "1|2|after|E94|Null|2", DisplayName = "counting up")]
    [DataRow("3 To 1 Step -1", "3|2|after|E94|Null|2", DisplayName = "counting down")]
    public async Task ANullCounter_GonePastUnderResumeNext_GoesOnAfterTheLoop(string header, string expected)
        => await AssertRunsAsync(Probe("On Error Resume Next", header), $"| {expected} |");

    [TestMethod]
    public async Task ANullCounter_TheHandlerResumingNext_GoesOnAfterTheLoop()
        // N09: the handler's Resume Next goes on after the Next that failed.
        => await AssertRunsAsync(Probe("On Error GoTo H", "1 To 3", Caught, "    Resume Next"), "| 1|2|E94@50|Null|after|E0|Null|2 |");

    [TestMethod]
    public async Task ANullCounter_NothingCatches_EndsTheProgramWithError94AtTheNext()
        => await AssertRunsAsync(Probe("' nothing catches", "1 To 3"), "| 94 ");

    // the program run in the interpreter and in compiled code, alike, as MS-VBA runs it; and Probe compiled.
    private static async Task AssertRunsAsync(string program, string expected)
    {
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter);
        StringAssert.Contains(interpreted.Observed, expected, interpreted.Observed);

        CompiledExecutionTests.RequirePayloads();
        var compiled = await RunAsync(program, VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        Assert.IsTrue(compiled.Compiled.TryGetValue("Probe", out var result), "Probe was not called");
        Assert.IsNotNull(result.Body, $"Probe: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
    }

    // Erl counts line-number labels, as MS-VBA's does.
    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> RunAsync(string program, VBExecutionEngine engine)
    {
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], program, (provider, run, _) =>
        {
            made = (Observed(run), CompiledExecutionTests.ByName(provider));
            return Task.CompletedTask;
        }, engine: engine, environment: profile => profile with { ErlLineNumbering = VBErlLineNumbering.LineLabel });
        return made;
    }
}
