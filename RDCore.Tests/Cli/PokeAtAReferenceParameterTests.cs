using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// A byte written where the program waits, at the address of a <c>ByRef</c> parameter (<c>VarPtr</c> of it), is written into the variable the caller passed:
/// the parameter is a second name for that variable (<strong>MS-VBAL §5.3.1.11</strong>), and the address is the variable's.
/// </summary>
[TestClass]
public sealed class PokeAtAReferenceParameterTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // what a run is to the person who runs it: what it printed, and the error it raised.
    private static string Observed(ExecuteResult result)
        => $"{result.Outcome} | {string.Join(" ; ", result.Output.Select(line => line.Trim()))} | {result.ErrorNumber} {result.ErrorMessage}";

    // Go passes n, holding 1, to Fill, which waits at a Stop with p = VarPtr(x) + offset; 42 is written at p, and the program goes on.
    private static async Task<string> PokedBelowFillAsync(string declaredType, int offset, string takenIn = "Fill")
    {
        var observed = string.Empty;
        await ModuleWorkspace.InspectAsync([], Program(
            "Public p As LongPtr",
            "Public Sub Main()",
            "    Debug.Print Go()",
            "End Sub",
            $"Private Function Go() As {declaredType}",
            $"    Dim n As {declaredType}",
            "    n = 1",
            takenIn == "Go" ? $"    p = VarPtr(n) + {offset}" : "",
            "    Fill n",
            "    Go = n",
            "End Function",
            $"Private Sub Fill(ByRef x As {declaredType})",
            takenIn == "Fill" ? $"    p = VarPtr(x) + {offset}" : "",
            "    Stop",
            "End Sub"), async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                var fill = provider.Execution.Stack().Frames.Single(frame => frame.Procedure == "Fill").Id;
                var address = int.Parse(provider.Execution.Variables(fill, DebugVariableScope.Module, 0).Variables.Single(variable => variable.Name == "p").Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                var poked = await new HostPokeHandler(provider).Handle(new HostPokeParams { Address = address, Value = 42 }, CancellationToken.None).WaitAsync(Patience);
                Assert.IsTrue(poked.IsWritten);
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
}
