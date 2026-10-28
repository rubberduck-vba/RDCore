using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.StdLib;
using static RDCore.Tests.Cli.ModuleWorkspace;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.36 StrComp</strong>: "an Integer indicating the result of a string comparison", -1, 0 or 1, and Null
/// when either String is Null.
/// </summary>
/// <remarks>
/// The comparison is the relational operators', so StrComp agrees with <c>&lt;</c> and <c>=</c> under the <c>Option Compare</c> its
/// Compare names.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.36 StrComp")]
public sealed class StrCompTests
{
    private const VBCompareMethod Binary = VBCompareMethod.VBBinaryCompare;
    private const VBCompareMethod Text = VBCompareMethod.VBTextCompare;

    [TestMethod]
    [DataRow("ABCD", "abcd", VBCompareMethod.VBTextCompare, 0)]
    [DataRow("ABCD", "abcd", VBCompareMethod.VBBinaryCompare, -1)]
    [DataRow("abcd", "ABCD", VBCompareMethod.VBBinaryCompare, 1)]
    [DataRow("B", "a", VBCompareMethod.VBBinaryCompare, -1)]
    [DataRow("B", "a", VBCompareMethod.VBTextCompare, 1)]
    [DataRow("", "", VBCompareMethod.VBBinaryCompare, 0)]
    [DataRow("a", "", VBCompareMethod.VBBinaryCompare, 1)]
    [DataRow("", "a", VBCompareMethod.VBBinaryCompare, -1)]
    public void StrComp_IsMinusOneZeroOrOne_AsAnInteger(string string1, string string2, VBCompareMethod compare, int expected)
        => Assert.AreEqual(expected, IntegerOf(Module().StrComp(V(string1), V(string2), compare)));

    [TestMethod]
    public void StrComp_OfNull_IsNull_WhateverTheCompare()
    {
        Assert.IsTrue(IsNull(Module().StrComp(VNull, V("a"), Binary)));
        Assert.IsTrue(IsNull(Module().StrComp(V("a"), VNull, Binary)));
        Assert.IsTrue(IsNull(Module().StrComp(VNull, VNull, Text)));
        Assert.IsTrue(IsNull(Module().StrComp(VNull, V("a"), (VBCompareMethod)5)));
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(-1)]
    [DataRow(240)]
    public void StrComp_WithACompareThatIsNoModeNorLocale_IsError5(int compare)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().StrComp(V("a"), V("A"), (VBCompareMethod)compare)));

    [TestMethod]
    [DataRow(7)]
    [DataRow(1033)]
    public void StrComp_WithACompareThatIsALocale_ComparesTheText(int compare)
        => Assert.AreEqual(0, IntegerOf(Module().StrComp(V("a"), V("A"), (VBCompareMethod)compare)));

    [TestMethod]
    public void StrComp_TakesEachValuesString()
    {
        Assert.AreEqual(0, IntegerOf(Module().StrComp(VEmpty, V(""), Binary)));
        Assert.AreEqual(0, IntegerOf(Module().StrComp(V(10), V("10"), Binary)));
        Assert.AreEqual(-1, IntegerOf(Module().StrComp(V(10), V("9"), Binary)));
        Assert.AreEqual(0, IntegerOf(Module().StrComp(V(true), V("True"), Binary)));
    }

    [TestMethod]
    public void StrComp_OfMissing_IsError13()
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().StrComp(VMissing, V("a"), Binary)));

    [TestMethod]
    public void StrComp_InBinaryMode_ALongerStringIsGreater()
        => Assert.AreEqual(1, IntegerOf(Module().StrComp(V("a\0"), V("a"), Binary)));

    [TestMethod]
    public void StrComp_InTextMode_IgnoresANullChar()
        => Assert.AreEqual(0, IntegerOf(Module().StrComp(V("a\0"), V("a"), Text)));

    [TestMethod]
    public void StrComp_InTextMode_FollowsTheEnvironmentsCulture()
    {
        // Turkish cases i to a dotted capital I; the invariant culture does not.
        Assert.AreEqual(0, IntegerOf(Module(lcid: 1055).StrComp(V("i"), V("İ"), Text)));
        Assert.AreNotEqual(0, IntegerOf(Module().StrComp(V("i"), V("İ"), Text)));
    }

    [TestMethod]
    [DataRow(VBCompareMethod.VBBinaryCompare)]
    [DataRow(VBCompareMethod.VBTextCompare)]
    public void StrComp_SeesATrailingByte(VBCompareMethod compare)
        // "A" and a byte: the same characters as "A", and more.
        => Assert.AreEqual(1, IntegerOf(Module().StrComp(V(Bytes(0x41, 0x00, 0x42)), V("A"), compare)));

    [TestMethod]
    public void StrComp_OfAOneByteString_SortsBeforeItsCharacter()
        // whichever order binary mode takes: no character before "A", or the byte 41 before 41 00.
        => Assert.AreEqual(-1, IntegerOf(Module().StrComp(V(Bytes(0x41)), V("A"), Binary)));

    [TestMethod]
    [DataRow(null, "1", "-1", DisplayName = "no Option Compare")]
    [DataRow(OptionCompare.Binary, "1", "-1", DisplayName = "Option Compare Binary")]
    [DataRow(OptionCompare.Text, "0", "1", DisplayName = "Option Compare Text")]
    [DataRow(OptionCompare.Database, "0", "1", DisplayName = "Option Compare Database")]
    public void StrComp_FromSource_WithCompareLeftOut_FollowsTheModulesOptionCompare(OptionCompare? mode, string sameLetter, string otherLetter)
        => CollectionAssert.AreEqual(new[] { sameLetter, otherLetter }, RunUnder(mode, "Debug.Print StrComp(\"a\", \"A\")", "Debug.Print StrComp(\"B\", \"a\")"));

    [TestMethod]
    [DataRow("\"a\"", "\"A\"")]
    [DataRow("\"B\"", "\"a\"")]
    [DataRow("ChrW(192)", "\"B\"")]
    [DataRow("ChrW(8364)", "ChrW(192)")]
    [DataRow("\"a\"", "\"a\" & Chr(0)")]
    [DataRow("\"i\"", "\"I\"")]
    public void StrComp_FromSource_AgreesWithTheRelationalOperators(string x, string y)
    {
        string[] body = [$"Debug.Print (StrComp({x}, {y}) = -1) = ({x} < {y})", $"Debug.Print (StrComp({x}, {y}) = 0) = ({x} = {y})"];

        CollectionAssert.AreEqual(new[] { "True", "True" }, RunUnder(OptionCompare.Binary, body));
        CollectionAssert.AreEqual(new[] { "True", "True" }, RunUnder(OptionCompare.Text, body));
    }

    [TestMethod]
    public void StrComp_FromSource_AnExplicitCompare_OverridesTheModules()
    {
        Assert.AreEqual("1", RunUnder(OptionCompare.Text, "Debug.Print StrComp(\"a\", \"A\", 0)").Single());
        Assert.AreEqual("0", RunUnder(OptionCompare.Binary, "Debug.Print StrComp(\"a\", \"A\", vbTextCompare)").Single());
        // V is Empty, which is the Compare 0.
        Assert.AreEqual("1", RunUnder(OptionCompare.Text, "Debug.Print StrComp(\"a\", \"A\", V)").Single());
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToErrorUnder(OptionCompare.Text, "Debug.Print StrComp(\"a\", \"a\", Null)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToErrorUnder(OptionCompare.Text, "Debug.Print StrComp(\"a\", \"A\", 2)"));
    }

    [TestMethod]
    public void StrComp_FromSource_OfNull_IsNull()
        => Assert.AreEqual("True", Run("Debug.Print IsNull(StrComp(Null, \"a\"))").Single());

    [TestMethod]
    public async Task StrComp_FromSource_InAClassModule_FollowsThatModulesOptionCompare()
    {
        var sorter = ClassModule("Sorter", "Option Explicit", "Option Compare Text",
            "Public Function Order(ByVal a As String, ByVal b As String) As Variant", "    Order = StrComp(a, b)", "End Function");
        var printed = await RunAsync([("Sorter", sorter)],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Public Sub Main()\r\n    Dim s As Sorter\r\n    Set s = New Sorter\r\n" +
            "    Debug.Print s.Order(\"a\", \"A\")\r\n    Debug.Print StrComp(\"a\", \"A\")\r\nEnd Sub\r\n");

        CollectionAssert.AreEqual(new[] { "0", "1" }, printed, string.Join(" | ", printed));
    }
}
