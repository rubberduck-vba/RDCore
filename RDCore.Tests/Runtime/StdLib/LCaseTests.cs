using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.17 LCase</strong>: "only uppercase letters are converted to lowercase; all lowercase letters and
/// non-letter characters remain unchanged", in the regional settings of the session's environment.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.17 LCase")]
[TestCategory("MS-VBAL 6.1.2.11.1.18 LCase$")]
public sealed class LCaseTests
{
    [TestMethod]
    [DataRow("Hello World 1234", "hello world 1234")]
    [DataRow("\u00C0\u00C9\u0391", "\u00E0\u00E9\u03B1")]
    [DataRow("\u0410", "\u0430")]
    public void LCase_CasesDownTheUppercaseLetters(string text, string expected)
        => Assert.AreEqual(expected, TextOf(Module().LCase(V(text))));

    [TestMethod]
    [DataRow("\u00DF")]
    [DataRow("\u00B5")]
    public void LCase_LeavesEverythingElse(string text)
        => Assert.AreEqual(text, TextOf(Module().LCase(V(text))));

    [TestMethod]
    public void LCase_OfNullEmptyOrANumber()
    {
        Assert.IsTrue(IsNull(Module().LCase(VNull)));
        Assert.AreEqual(string.Empty, TextOf(Module().LCase(VEmpty)));
        Assert.AreEqual("123", TextOf(Module().LCase(V(123))));
    }

    [TestMethod]
    public void LCase_UnderTheEnvironmentsCulture()
    {
        Assert.AreEqual("\u0131", TextOf(Module(lcid: 1055).LCase(V("I"))), "Turkish has a dotless i");
        Assert.AreEqual("i", TextOf(Module().LCase(V("I"))));
    }

    [TestMethod]
    public void LCaseString_OfNull_IsError94()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().LCaseString(VNull)));

        var twin = Module().LCaseString(V("A"));
        Assert.IsInstanceOfType<VBStringValue>(twin.Result);
        Assert.AreEqual("a", twin.Result.Value);
    }

    [TestMethod]
    public void LCaseDollar_FromSource()
    {
        CollectionAssert.AreEqual(new[] { "Null", "true", "[]" }, Run(
            "Debug.Print LCase(Null)", "Debug.Print LCase$(True)", "Debug.Print \"[\" & LCase$(V) & \"]\""));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print LCase$(Null)"));
    }
}
