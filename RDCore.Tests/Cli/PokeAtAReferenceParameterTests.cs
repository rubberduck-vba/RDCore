using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Platform.Components;

namespace RDCore.Tests.Cli;

/// <summary>
/// A byte written where the program waits, at the address of a <c>ByRef</c> parameter (<c>VarPtr</c> of it), is written into the variable the caller passed:
/// the parameter is a second name for that variable (<strong>MS-VBAL §5.3.1.11</strong>), and the address is the variable's.
/// </summary>
[TestClass]
public sealed class PokeAtAReferenceParameterTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // what a run is to the person who runs it: what it printed, and the error it raised.
    private static string Observed(ExecuteResult result)
        => $"{result.Outcome} | {string.Join(" ; ", result.Output.Select(line => line.Trim()))} | {result.ErrorNumber} {result.ErrorMessage}";

    // Go passes n, holding value, to Fill, which waits at a Stop with p = VarPtr(x) + offset and q = StrPtr(other), other being "xy"; the bytes are written from
    // p on - 42 unless the row says what, from the module's variables - and the program goes on to print what Go returned (r).
    private static async Task<string> PokedBelowFillAsync(string declaredType, int offset, string takenIn = "Fill", string value = "1", string printed = "r",
        Func<Func<string, long>, byte[]>? bytes = null)
    {
        var observed = string.Empty;
        await ModuleWorkspace.InspectAsync([], Program(
            "Public p As LongPtr",
            "Public q As LongPtr",
            "Public other As String",
            "Public Sub Main()",
            "    Dim r As Variant",
            "    r = Go()",
            $"    Debug.Print {printed}",
            "End Sub",
            $"Private Function Go() As {declaredType}",
            $"    Dim n As {declaredType}",
            $"    n = {value}",
            takenIn == "Go" ? $"    p = VarPtr(n) + {offset}" : "",
            "    Fill n",
            "    Go = n",
            "End Function",
            $"Private Sub Fill(ByRef x As {declaredType})",
            "    other = \"xy\"",
            "    q = StrPtr(other)",
            takenIn == "Fill" ? $"    p = VarPtr(x) + {offset}" : "",
            "    Stop",
            "End Sub"), async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                var fill = provider.Execution.Stack().Frames.Single(frame => frame.Procedure == "Fill").Id;
                var module = provider.Execution.Variables(fill, DebugVariableScope.Module, 0).Variables;
                long ValueOf(string name) => long.Parse(module.Single(variable => variable.Name == name).Value, System.Globalization.CultureInfo.InvariantCulture);
                var address = (int)ValueOf("p");
                foreach (var (written, at) in (bytes?.Invoke(ValueOf) ?? [42]).Select((written, at) => (written, at)))
                {
                    var poked = await new HostPokeHandler(provider).Handle(new HostPokeParams { Address = address + at, Value = written }, CancellationToken.None)
                        .WaitAsync(Patience);
                    Assert.IsTrue(poked.IsWritten, $"the byte at p + {at}");
                }

                observed = Observed(await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                    .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience));
            }
            finally
            {
                if (provider.Execution.State is not ProgramState.Idle)
                {
                    _ = await new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);
                }
            }
        }, debug: true);
        return observed;
    }

    [TestMethod]
    [DataRow("Long", 0, DisplayName = "a Long")]
    [DataRow("Integer", 0, DisplayName = "an Integer")]
    [DataRow("Byte", 0, DisplayName = "a Byte")]
    [DataRow("Variant", 8, DisplayName = "a Variant holding an Integer, at its data")]
    public async Task AByteWrittenAtTheAddressOfAParameterPassedByReference_IsWrittenIntoTheCallersVariable(string declaredType, int offset)
        => StringAssert.Contains(await PokedBelowFillAsync(declaredType, offset), "| 42 |");

    [TestMethod]
    [DataRow("Long", 0, DisplayName = "a Long")]
    [DataRow("Variant", 8, DisplayName = "a Variant holding an Integer, at its data")]
    public async Task AByteWrittenAtTheAddressTheCallerTookOfItsVariable_IsWhatTheCallerReads(string declaredType, int offset)
        => StringAssert.Contains(await PokedBelowFillAsync(declaredType, offset, takenIn: "Go"), "| 42 |");

    // the data of a VARIANT holding a String is the pointer the String is: the address of the characters of another BSTR makes it that String.
    [TestMethod]
    public async Task ThePointerOfAStringInAVariantParameter_WrittenWithTheAddressOfAnotherString_MakesTheCallersVariantThatString()
        => StringAssert.Contains(await PokedBelowFillAsync("Variant", 8, value: "\"ab\"", bytes: valueOf => BitConverter.GetBytes(valueOf("q"))), "| xy |");

    // the first two bytes of a VARIANT are its VarType: vbLong (3) over an Integer's makes it a Long of the bytes it holds.
    [TestMethod]
    public async Task TheTypeOfAVariantParameter_Written_IsTheTypeOfTheCallersVariant()
        => StringAssert.Contains(await PokedBelowFillAsync("Variant", 0, printed: "TypeName(r) & \" \" & r", bytes: _ => [3]), "| Long 1 |");

    // a program that runs owns its memory: a byte is written at a stop, or with no program.
    [TestMethod]
    public async Task AByteWritten_WhileAProgramRuns_IsRefused()
        => await ModuleWorkspace.InspectAsync([], Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Do While True",
            "        Total = Total + 1",
            "    Loop",
            "End Sub"), async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, "the cancellation of the request is a break");
            var goesOn = ModuleWorkspace.WhenRunning(provider);
            var running = new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);
            await goesOn.WaitAsync(Patience);

            var poked = await new HostPokeHandler(provider).Handle(new HostPokeParams { Address = 16, Value = 42 }, CancellationToken.None).WaitAsync(Patience);

            Assert.IsFalse(poked.IsWritten);
            Assert.AreEqual(RDCore.Runtime.Support.Resources.Host_TheProgramIsRunning, poked.Error);
            _ = await new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);
            Assert.AreEqual(ExecutionOutcome.Halted, (await running).Outcome);
        }, ModuleWorkspace.WhenRunning, debug: true);
}
