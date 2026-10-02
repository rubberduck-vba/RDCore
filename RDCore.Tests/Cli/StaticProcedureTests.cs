using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.3.1.2</strong> a procedure declared <c>Static</c>: every local variable of it has module extent, and keeps its value from one call to the next -
/// not only the ones declared with the <c>Static</c> keyword.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.2 Static Procedures")]
public sealed class StaticProcedureTests
{
    // Main calls the procedure under test twice and the rest of the module is declared by closing Main early: RunAsync wraps `body` in Main.
    private static async Task<string[]> RunAsync(string declaration, string footer, string procedureBody, string calls = "Counter\r\nCounter")
    {
        var body = $"{calls}\r\nEnd Sub\r\n{declaration}\r\n{procedureBody}\r\n{footer}\r\nPublic Sub Unused()";
        var result = await SupportedLanguageTests.RunAsync(expression: "", statement: body);

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        return [.. result.Output.Select(line => line.Trim())];
    }

    [TestMethod]
    public async Task ALocalOfAStaticSub_KeepsItsValueAcrossCalls()
        => CollectionAssert.AreEqual(new[] { "1", "2" },
            await RunAsync("Public Static Sub Counter()", "End Sub", "Dim n As Long\r\nn = n + 1\r\nDebug.Print n"));

    [TestMethod]
    public async Task ALocalOfAProcedureThatIsNotStatic_StartsOverEveryCall()
        => CollectionAssert.AreEqual(new[] { "1", "1" },
            await RunAsync("Public Sub Counter()", "End Sub", "Dim n As Long\r\nn = n + 1\r\nDebug.Print n"));

    [TestMethod]
    public async Task ALocalWithTheStaticKeyword_StillKeepsItsValue_InAProcedureThatIsNot()
        => CollectionAssert.AreEqual(new[] { "1", "2" },
            await RunAsync("Public Sub Counter()", "End Sub", "Static n As Long\r\nn = n + 1\r\nDebug.Print n"));

    [TestMethod]
    public async Task AnImplicitlyDeclaredLocal_OfAStaticSub_KeepsItsValue()
        => CollectionAssert.AreEqual(new[] { "1", "2" },
            await RunAsync("Public Static Sub Counter()", "End Sub", "n = n + 1\r\nDebug.Print n"));

    [TestMethod]
    public async Task ALocalOfAStaticFunction_KeepsItsValueAcrossCalls()
        => CollectionAssert.AreEqual(new[] { "1", "2" },
            await RunAsync("Public Static Function Next_() As Long", "End Function",
                "Dim n As Long\r\nn = n + 1\r\nNext_ = n", calls: "Debug.Print Next_()\r\nDebug.Print Next_()"));

    [TestMethod]
    public async Task EachStaticProcedure_HasItsOwnLocals()
        => CollectionAssert.AreEqual(new[] { "1", "1", "2", "2" },
            await RunAsync("Public Static Sub A()", "End Sub",
                "Dim n As Long\r\nn = n + 1\r\nDebug.Print n\r\nEnd Sub\r\nPublic Static Sub B()\r\nDim n As Long\r\nn = n + 1\r\nDebug.Print n",
                calls: "A\r\nB\r\nA\r\nB"));

    [TestMethod]
    public async Task AParameterOfAStaticSub_IsStillANewValueEveryCall()
        => CollectionAssert.AreEqual(new[] { "5", "7" },
            await RunAsync("Public Static Sub Counter(ByVal x As Long)", "End Sub", "Debug.Print x", calls: "Counter 5\r\nCounter 7"));
}
