using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Single</c> or a <c>Double</c> joined to a <c>String</c> is let-coerced to <c>String</c> by compiled code (MS-VBAL §5.6.9.4, §5.5.1.2.4), as the
/// interpreter's let-coercion writes it: with the decimal symbol of the session's regional settings, at most 7 significant digits of a <c>Single</c> and 15
/// of a <c>Double</c>, and in scientific notation past them. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledFloatingTextTests
{
    private static readonly string Joining = Program(
        "Public Sub Main()",
        "    Debug.Print Joined(3)",
        "End Sub",
        "Private Function Joined(ByVal N As Long) As String",
        "    Dim I As Long, D As Double, S As Single, T As String",
        "    For I = 1 To N",
        "        D = I / 4",
        "        S = I / 3",
        "        T = T & D & \";\" & S & \";\"",
        "    Next",
        "    D = 1E-50",
        "    T = T & D & \";\"",
        "    D = 1.23456789012345E+17",
        "    T = T & D & \";\"",
        "    D = -2.5",
        "    Joined = T & D",
        "End Function");

    // the same program in each engine, its text alike, and Joined compiled: no statement stepped by the interpreter, none recovered.
    [TestMethod]
    [DataRow(".", "| 0.25;0.3333333;0.5;0.6666667;0.75;1;", DisplayName = "a period")]
    [DataRow(",", "| 0,25;0,3333333;0,5;0,6666667;0,75;1;", DisplayName = "a comma")]
    public async Task ASingleOrADoubleJoinedToAString_IsWrittenInTheRegionalSettings(string decimalSymbol, string expected)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(VBExecutionEngine.Interpreter, decimalSymbol);
        var (observed, compiled) = await RunAsync(VBExecutionEngine.Compiled, decimalSymbol);
        Assert.AreEqual(interpreted.Observed, observed);
        Assert.Contains(expected, observed);

        Assert.IsTrue(compiled.TryGetValue("Joined", out var result), "Joined was not called");
        Assert.IsNotNull(result.Body, $"Joined: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        Assert.IsEmpty(result.Stepped, $"Joined steps: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(0, result.Body.Recoveries, "Joined's recoveries");
        }
    }

    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> RunAsync(
        VBExecutionEngine engine, string decimalSymbol)
    {
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], Joining, (provider, run, _) =>
        {
            made = (Observed(run), CompiledExecutionTests.ByName(provider));
            return Task.CompletedTask;
        }, engine: engine, environment: profile => profile with { RegionalSettings = RegionalSettings.Default with { DecimalSymbol = decimalSymbol } });
        return made;
    }
}
