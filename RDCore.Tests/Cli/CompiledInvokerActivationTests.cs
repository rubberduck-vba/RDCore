using RDCore.Runtime.Compiled;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A compiled procedure called through the invoker, as every one given a <c>String</c> is, is run in the activation that last returned from a call of it
/// when that one ran its compiled code to the end: each call begins with its own locals, result and error handling (MS-VBAL §5.3.1.10), and one that
/// is handed to the interpreter, or that is still running, is not run again.
/// </summary>
[TestClass]
public sealed class CompiledInvokerActivationTests
{
    // the program run to its end with engine: what it did, and what measure reads of the bodies compiled for it.
    private static async Task<(string Observed, long Measured)> RunAsync(string program, VBExecutionEngine engine, Func<EnvironmentSessionProvider, long>? measure)
    {
        (string Observed, long Measured) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], program, (provider, run, _) =>
        {
            made = (Observed(run), engine is VBExecutionEngine.Compiled && measure is not null ? measure(provider) : 0);
            return Task.CompletedTask;
        }, engine: engine);
        return made;
    }

    // the program run both ways: the compiled run is the interpreted one.
    private static async Task<(string Observed, long Measured)> AssertAlikeAsync(string program, Func<EnvironmentSessionProvider, long> measure)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, null);
        var compiled = await RunAsync(program, VBExecutionEngine.Compiled, measure);
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        return compiled;
    }

    // Tag is given a String, so Go calls it through the invoker, four times. The first call sets On Error Resume Next, assigns a local and returns a result;
    // the second, run again in the first's activation, starts with neither, and returns the default String. The third raises an error, which the first's
    // On Error would have resumed: it is Go's to handle, and the interpreter is given the call. The fourth is made in an activation of its own.
    [TestMethod]
    public async Task ACallMadeAgainInTheLastActivation_BeginsWithItsOwnLocalsResultAndErrorHandling()
    {
        var (observed, made) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Go()",
            "End Sub",
            "Private Function Go() As String",
            "    Dim i As Long, s As String",
            "    On Error Resume Next",
            "    For i = 1 To 4",
            "        s = s & Tag(\"x\" & i, i) & \";\"",
            "        If Err.Number <> 0 Then s = s & \"e\" & Err.Number & \";\": Err.Clear",
            "    Next",
            "    Go = s",
            "End Function",
            "Private Function Tag(ByVal t As String, ByVal i As Long) As String",
            "    Dim kept As String",
            "    If i = 1 Then",
            "        On Error Resume Next",
            "        kept = \"k\"",
            "    End If",
            "    If i = 3 Then i = i \\ (i - i)",
            "    If i Mod 2 = 1 Then Tag = t & kept",
            "End Function"), provider => BodyOf(provider, "Tag").InvokerActivationsMade);

        StringAssert.Contains(observed, "| x1k;;e11;; |");
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(2L, made, "the first call's activation, run again for the second and the third; and the fourth's");
        }
    }

    // Path calls itself through the invoker, given a String: each activation of the recursion is its own, the one still running never run again for the
    // call it makes, and the calls after it are made in the activation that returned last.
    [TestMethod]
    public async Task ARecursionThroughTheInvoker_RunsEachCallInAnActivationOfItsOwn()
    {
        var (observed, made) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Path(\"a\", 3) & \"|\" & Path(\"b\", 2) & \"|\" & Path(\"c\", 0)",
            "End Sub",
            "Private Function Path(ByVal s As String, ByVal n As Long) As String",
            "    If n = 0 Then",
            "        Path = s",
            "    Else",
            "        Path = Path(s & n, n - 1) & \"<\" & n",
            "    End If",
            "End Function"), provider => BodyOf(provider, "Path").InvokerActivationsMade);

        StringAssert.Contains(observed, "| a321<1<2<3|b21<1<2|c |");
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.IsGreaterThanOrEqualTo(4L, made, "as many as the recursion goes deep");
            Assert.IsLessThan(8L, made, "fewer than the eight calls");
        }
    }
}
