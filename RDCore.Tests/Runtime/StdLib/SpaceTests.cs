using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.33 Space</strong>: "a String consisting of the specified number of spaces".
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.33 Space")]
[TestCategory("MS-VBAL 6.1.2.11.1.34 Space$")]
public sealed class SpaceTests
{
    [TestMethod]
    public void Space_IsThatManySpaces()
    {
        Assert.AreEqual("   ", TextOf(Module().Space(L(3))));
        Assert.AreEqual(string.Empty, TextOf(Module().Space(L(0))));

        var twin = Module().SpaceString(L(3));
        Assert.IsInstanceOfType<VBStringValue>(twin.Result);
        Assert.AreEqual("   ", twin.Result.Value);
    }

    [TestMethod]
    public void Space_OfANegativeNumber_IsError5()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Space(L(-1))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().SpaceString(L(-1))));
    }

    [TestMethod]
    public void Space_LongerThanAStringCanHold_IsError14()
        => Assert.AreEqual((int)VBRuntimeErrorId.OutOfStringSpace, ErrorOf(Module().Space(L(int.MaxValue))));

    [TestMethod]
    public void Space_FromSource()
    {
        // 2.5 rounds to the even 2; Empty is 0.
        CollectionAssert.AreEqual(new[] { "[  ]", "[   ]", "[]", "[   ]" }, Run(
            "Debug.Print \"[\" & Space(2.5) & \"]\"",
            "Debug.Print \"[\" & Space(\"3\") & \"]\"",
            "Debug.Print \"[\" & Space(V) & \"]\"",
            "Debug.Print \"[\" & Space$(3) & \"]\""));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print Space(Null)"));
    }
}
