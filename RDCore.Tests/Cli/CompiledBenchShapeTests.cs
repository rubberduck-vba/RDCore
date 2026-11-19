using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The loops of the bench's <c>IsEmpty</c>, <c>Variant</c> arithmetic and module and <c>Static</c> variable cases, run by compiled code from start to end:
/// no statement stepped by the interpreter or recovered, no walk back, and no call of the standard library's function or support call. A program runs
/// the same either way.
/// </summary>
[TestClass]
public sealed class CompiledBenchShapeTests
{
    private const int Passes = 1000;

    private static readonly string Shapes = Program(
        "Private Total As Long",
        "Public Sub Main()",
        "    Debug.Print Emptied(); Summed(); SummedOfLongs(); Mixed(); KeptStatic(); KeptInModule()",
        "End Sub",
        "Private Function Emptied() As Boolean",
        "    Dim I As Long, V As Variant, B As Boolean",
        $"    For I = 1 To {Passes}",
        "        B = IsEmpty(V)",
        "    Next",
        "    Emptied = B",
        "End Function",
        "Private Function SummedOfLongs() As Variant",
        "    Dim I As Long, S As Variant",
        "    S = 0#",
        $"    For I = 1 To {Passes}",
        "        S = S + I",
        "    Next",
        "    SummedOfLongs = S",
        "End Function",
        "Private Function Summed() As Variant",
        "    Dim I As Variant, S As Variant",
        "    S = 0#",
        $"    For I = 1 To {Passes}",
        "        S = S + I",
        "    Next",
        "    Summed = S",
        "End Function",
        "Private Function Mixed() As Variant",
        "    Dim I As Long, D As Double, V As Variant",
        "    V = 0",
        "    D = 0.5",
        $"    For I = 1 To {Passes}",
        "        V = V + I * D",
        "    Next",
        "    Mixed = V",
        "End Function",
        "Private Function KeptStatic() As Long",
        "    Dim I As Long",
        "    Static Kept As Long",
        "    Kept = 0",
        $"    For I = 1 To {Passes}",
        "        Kept = Kept + 1",
        "    Next",
        "    KeptStatic = Kept",
        "End Function",
        "Private Function KeptInModule() As Long",
        "    Dim I As Long",
        "    Total = 0",
        $"    For I = 1 To {Passes}",
        "        Total = Total + 1",
        "    Next",
        "    KeptInModule = Total",
        "End Function");

    // the program run both ways, and procedure compiled, run by compiled code alone.
    private static async Task AssertRunByCompiledCodeAsync(string procedure)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(VBExecutionEngine.Interpreter);
        var (observed, compiled) = await RunAsync(VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted.Observed, observed);
        Assert.Contains("500500", observed);

        Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
        Assert.IsNotNull(result.Body, $"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        Assert.IsEmpty(result.Stepped, $"{procedure} steps: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        if (CompiledExecution.HandsOverAtRandom)
        {
            return;
        }

        Assert.AreEqual(0, result.Body.Recoveries, $"{procedure}'s recoveries");
        Assert.AreEqual(0, result.Body.WalkBacks, $"{procedure}'s walk-backs");
        Assert.AreEqual(0, result.Body.LibraryCallsMade, $"{procedure}'s calls of the library");
        Assert.AreEqual(0, result.Body.SupportCallsMade, $"{procedure}'s support calls");
    }

    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> RunAsync(VBExecutionEngine engine)
    {
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], Shapes, (provider, run, _) =>
        {
            made = (Observed(run), CompiledExecutionTests.ByName(provider));
            return Task.CompletedTask;
        }, engine: engine);
        return made;
    }

    // MS-VBAL §6.1.2.7.1.4: IsEmpty of a Variant that was never assigned, read in its cell.
    [TestMethod]
    public async Task IsEmptyOfAVariant_InALoop_IsRunByCompiledCode()
        => await AssertRunByCompiledCodeAsync("Emptied");

    // MS-VBAL §5.6.9.3.2: a Variant plus a Long, the loop's counter.
    [TestMethod]
    public async Task ASumOfAVariantAndALong_InALoop_IsRunByCompiledCode()
        => await AssertRunByCompiledCodeAsync("SummedOfLongs");

    // MS-VBAL §5.6.9.3.2, §5.4.2.3: the sum of two Variants, the loop's counter a Variant too.
    [TestMethod]
    [Ignore("A For loop whose counter is a Variant is stepped by the interpreter (ForCounter).")]
    public async Task ASumOfVariants_InALoopOfAVariantCounter_IsRunByCompiledCode()
        => await AssertRunByCompiledCodeAsync("Summed");

    // MS-VBAL §5.6.9.3.2, §5.6.9.3.4: a Variant plus a Double product of a Long and a Double.
    [TestMethod]
    public async Task AVariantPlusADouble_InALoop_IsRunByCompiledCode()
        => await AssertRunByCompiledCodeAsync("Mixed");

    // MS-VBAL §5.4.3.1: a Static variable, which its cell keeps across calls.
    [TestMethod]
    public async Task AStaticVariable_InALoop_IsRunByCompiledCode()
        => await AssertRunByCompiledCodeAsync("KeptStatic");

    // MS-VBAL §5.2.3.1: a variable of the module, which its cell holds.
    [TestMethod]
    public async Task AModuleVariable_InALoop_IsRunByCompiledCode()
        => await AssertRunByCompiledCodeAsync("KeptInModule");
}
