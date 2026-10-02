namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.3.3</strong> "If the redimensioned variable is currently locked by a ByRef formal parameter runtime Error 10 is raised."
/// </summary>
/// <remarks>
/// An array passed by reference is the parameter's for as long as the call lasts, so another name for it - the module variable the argument is, a
/// public variable of an object - cannot take its dimensions away. Re-dimensioning it through the parameter is what passing it by reference is for.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim Statement")]
public sealed class ReDimArrayLockTests
{
    private const string Module = "Public arr() As Long";

    private static Task<string[]> RunAsync(string procedures, params string[] body)
        => ArrayStatementTargetTests.RunProgramAsync(Module, procedures, body);

    [TestMethod]
    public async Task ReDim_OfTheVariableAnArgumentIs_WhileTheCalleeHoldsItByRef_IsError10()
        => CollectionAssert.AreEqual(new[] { "10" },
            await RunAsync(
                "Public Sub Hold(ByRef p() As Long)\r\nOn Error Resume Next\r\nReDim arr(1 To 5)\r\nDebug.Print Err.Number\r\nEnd Sub",
                "ReDim arr(1 To 3)", "Hold arr"));

    [TestMethod]
    public async Task ReDim_OfThePublicVariableOfAnObject_WhileACalleeHoldsItByRef_IsError10()
        => CollectionAssert.AreEqual(new[] { "10" },
            await RunAsync(
                "Public Sub Hold(ByRef p() As Long, ByVal o As Box)\r\nOn Error Resume Next\r\nReDim o.Items(1 To 5)\r\nDebug.Print Err.Number\r\nEnd Sub",
                "ReDim b.Items(1 To 3)", "Hold b.Items, b"));

    [TestMethod]
    public async Task ReDim_OfTheParameterItself_IsWhatPassingItByRefIsFor()
        => CollectionAssert.AreEqual(new[] { "9" },
            await RunAsync(
                "Public Sub Grow(ByRef p() As Long)\r\nReDim p(1 To 9)\r\nEnd Sub",
                "ReDim arr(1 To 3)", "Grow arr", "Debug.Print UBound(arr)"));

    [TestMethod]
    public async Task ReDim_OfAParameterThatWasPassedDownAChainOfByRefCalls_IsStillTheParameters()
        => CollectionAssert.AreEqual(new[] { "7" },
            await RunAsync(
                "Public Sub First(ByRef p() As Long)\r\nSecond p\r\nEnd Sub\r\nPublic Sub Second(ByRef q() As Long)\r\nReDim q(1 To 7)\r\nEnd Sub",
                "ReDim arr(1 To 3)", "First arr", "Debug.Print UBound(arr)"));

    [TestMethod]
    public async Task ReDim_OfTheVariable_OnceTheCallHasReturned_IsNoLongerLocked()
        => CollectionAssert.AreEqual(new[] { "6" },
            await RunAsync(
                "Public Sub Hold(ByRef p() As Long)\r\nEnd Sub",
                "ReDim arr(1 To 3)", "Hold arr", "ReDim arr(1 To 6)", "Debug.Print UBound(arr)"));

    [TestMethod]
    public async Task ReDim_OfAVariableNoParameterHolds_IsNeverLocked()
        => CollectionAssert.AreEqual(new[] { "4" },
            await RunAsync(
                "Public Sub Other(ByRef p() As Long)\r\nReDim arr(1 To 4)\r\nEnd Sub",
                "Dim mine() As Long", "ReDim mine(1 To 2)", "Other mine", "Debug.Print UBound(arr)"));
}
