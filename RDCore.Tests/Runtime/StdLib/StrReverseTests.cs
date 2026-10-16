using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.40 StrReverse</strong>: "a String in which the character order of a specified String is reversed".
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.40 StrReverse")]
public sealed class StrReverseTests
{
    [TestMethod]
    [DataRow("abc", "cba")]
    [DataRow("", "")]
    public void StrReverse_ReversesTheCharacters(string text, string expected)
        => Assert.AreEqual(expected, TextOf(Module().StrReverse(new VBStringValue(text))));

    [TestMethod]
    public void StrReverse_ReversesCodeUnits()
    {
        var pair = char.ConvertFromUtf32(0x1F600);

        Assert.AreEqual($"b{pair[1]}{pair[0]}a", TextOf(Module().StrReverse(new VBStringValue($"a{pair}b"))), "a surrogate pair");
        Assert.AreEqual("b\u0301ea", TextOf(Module().StrReverse(new VBStringValue("ae\u0301b"))), "a combining mark");
    }

    [TestMethod]
    public void StrReverse_FromSource()
    {
        CollectionAssert.AreEqual(new[] { "[]", "321", "5.1" }, Run(
            "Debug.Print \"[\" & StrReverse(V) & \"]\"",
            "Debug.Print StrReverse(123)",
            "Debug.Print StrReverse(1.5)"));
        // "If Expression is Null, an error occurs."
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print StrReverse(Null)"));
    }
}
