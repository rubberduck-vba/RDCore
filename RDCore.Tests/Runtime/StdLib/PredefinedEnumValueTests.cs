using RDCore.Runtime.Execution;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.1</strong> a predefined enum's constants, read from VBA source: each one is the <c>Long</c> its
/// declaration states (<strong>MS-VBAL §5.2.3.4</strong>), resolvable unqualified.
/// </summary>
/// <remarks>
/// A constant has no storage to read a value from: the standard library's symbol carries the value its declaration states,
/// and the evaluator reads it from there at each use site.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.1 Predefined Enums")]
public sealed class PredefinedEnumValueTests
{
    /// <summary>Runs a body with the standard library available, and reports what it printed.</summary>
    private static IReadOnlyList<string> Run(params string[] body)
    {
        var output = new RuntimeOutputBuffer();

        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [], output, standardLibrary: true, body);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind,
            $"{outcome.ErrorInfo?.ErrorId} {outcome.ErrorInfo?.Description} | {outcome.ErrorInfo?.Verbose}");
        return output.Lines;
    }

    [TestMethod]
    [DataRow("vbTextCompare", "1")]
    [DataRow("vbProperCase", "3")]
    [DataRow("vbUseSystemDayOfWeek", "0")]
    public void APredefinedEnumConstant_PrintsItsValue(string constant, string expected)
        => Assert.AreEqual(expected, Run($"Debug.Print {constant}").Single().Trim());

    [TestMethod]
    [DataRow("VbCompareMethod.vbTextCompare", DisplayName = "by the name of its enum")]
    [DataRow("VBA.vbTextCompare", DisplayName = "by the name of its library")]
    [DataRow("VBA.VbCompareMethod.vbTextCompare", DisplayName = "by both")]
    public void APredefinedEnumConstant_Qualified_PrintsItsValue(string expression)
        => Assert.AreEqual("1", Run($"Debug.Print {expression}").Single().Trim());

    [TestMethod]
    public void APredefinedEnumConstant_ComparesAsItsValue()
    {
        var output = Run("If vbTextCompare = 1 Then", "Debug.Print \"equal\"", "End If");

        Assert.AreEqual("equal", output.Single().Trim());
    }

    [TestMethod]
    public void APredefinedEnumConstant_IsALongInArithmetic()
    {
        // 1 * 32767 + 32767 is 65534, which only a Long holds: were the constant an Integer, the whole expression would
        // be Integer arithmetic, and overflow.
        var output = Run("Debug.Print vbTextCompare * 32767 + 32767");

        Assert.AreEqual("65534", output.Single().Trim());
    }

    [TestMethod]
    public void APredefinedEnumConstant_IsPassedWhereItsEnumIsDeclared()
    {
        // StrComp's Compare is declared As VbCompareMethod: the constant's value is Let-coerced to it on the way in,
        // and arrives as the Long it is.
        RuntimeSourceHarness.RecordingDispatcher? spy = null;

        RuntimeSourceHarness.Run(
            fileSystem: null, [], output: null, standardLibrary: true, arrange: null,
            new RuntimeSourceHarness.Surroundings { Dispatcher = inner => spy = new RuntimeSourceHarness.RecordingDispatcher(inner) },
            "Debug.Print StrComp(\"a\", \"A\", vbTextCompare)");

        Assert.IsNotNull(spy);
        Assert.AreEqual(1, spy.Requests.Single().Arguments[2].BoxedValue);
    }
}
