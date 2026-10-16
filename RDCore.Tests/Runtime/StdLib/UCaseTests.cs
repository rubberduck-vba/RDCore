using RDCore.SDK.Model.Errors;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.41 UCase</strong>: "only lowercase letters are converted to uppercase; all uppercase letters and
/// non-letter characters remain unchanged", in the regional settings of the session's environment, one character for one.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.41 UCase")]
[TestCategory("MS-VBAL 6.1.2.11.1.42 UCase$")]
public sealed class UCaseTests
{
    [TestMethod]
    [DataRow("Hello World 1234", "HELLO WORLD 1234")]
    [DataRow("\u00E9", "\u00C9")]
    [DataRow("\uFF41", "\uFF21")]
    public void UCase_CasesUpTheLowercaseLetters(string text, string expected)
        => Assert.AreEqual(expected, TextOf(Module().UCase(V(text))));

    [TestMethod]
    public void UCase_KeepsTheLengthOfEveryCharacter()
    {
        var pair = char.ConvertFromUtf32(0x1F600);

        Assert.AreEqual("\u00DF", TextOf(Module().UCase(V("\u00DF"))), "no SS");
        Assert.AreEqual($"A{pair}", TextOf(Module().UCase(V($"a{pair}"))));
    }

    [TestMethod]
    public void UCase_OfNullEmptyOrABoolean()
    {
        Assert.IsTrue(IsNull(Module().UCase(VNull)));
        Assert.AreEqual(string.Empty, TextOf(Module().UCase(VEmpty)));
        Assert.AreEqual("TRUE", TextOf(Module().UCase(V(true))));
    }

    [TestMethod]
    public void UCase_UnderTheEnvironmentsCulture()
    {
        Assert.AreEqual("\u0130", TextOf(Module(lcid: 1055).UCase(V("i"))), "Turkish has a dotted capital I");
        Assert.AreEqual("I", TextOf(Module().UCase(V("i"))));
    }

    [TestMethod]
    public void UCase_OfMissing_IsError13()
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().UCase(VMissing)));

    [TestMethod]
    public void UCaseDollar_FromSource()
    {
        Assert.AreEqual("TRUE", Run("Debug.Print UCase$(True)").Single());
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print UCase$(Null)"));
    }
}
