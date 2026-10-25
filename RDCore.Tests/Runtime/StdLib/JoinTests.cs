using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using static RDCore.Tests.Cli.ModuleWorkspace;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.16 Join</strong>: "a string created by joining a number of substrings contained in an array",
/// separated by a Delimiter that is a space when it is left out.
/// </summary>
/// <remarks>
/// SourceArray is declared <c>SourceArray() As Variant</c> and is a <c>Variant</c> in the library, so that the <c>String()</c>
/// <c>Split</c> returns reaches it; the member checks for itself that it is given a one-dimensional array.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.16 Join")]
public sealed class JoinTests
{
    [TestMethod]
    public void Join_SeparatesTheElementsWithTheDelimiter()
    {
        Assert.AreEqual("a-b", TextOf(Module().Join(StringArray("a", "b"), V("-"))));
        Assert.AreEqual("a--b", TextOf(Module().Join(StringArray("a", "b"), V("--"))));
    }

    [TestMethod]
    public void Join_WithTheDelimiterLeftOut_UsesASpace()
        => Assert.AreEqual("a b", TextOf(Module().Join(StringArray("a", "b"))));

    [TestMethod]
    public void Join_WithAZeroLengthOrEmptyDelimiter_UsesNone()
    {
        Assert.AreEqual("ab", TextOf(Module().Join(StringArray("a", "b"), V(string.Empty))));
        Assert.AreEqual("ab", TextOf(Module().Join(StringArray("a", "b"), VEmpty)));
    }

    [TestMethod]
    public void Join_ANonStringDelimiter_IsItsString_AndNullIsError94()
    {
        Assert.AreEqual("a0b", TextOf(Module().Join(StringArray("a", "b"), V(0))));
        Assert.AreEqual("a1.5b", TextOf(Module().Join(StringArray("a", "b"), V(1.5))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().Join(StringArray("a", "b"), VNull)));
    }

    [TestMethod]
    public void Join_TakesEachElementsString_InTheRegionalSettings()
    {
        var elements = VariantArray(new VBLongValue(1), new VBDoubleValue(2.5), new VBBooleanValue(true));

        Assert.AreEqual("1 2.5 True", TextOf(Module().Join(elements)));
        Assert.AreEqual("1 2,5 True", TextOf(Module(lcid: 1031, regional: German).Join(elements)));
        Assert.AreEqual("1 2.5 True", TextOf(Module(lcid: 1031).Join(elements)), "a culture is no regional settings");
    }

    [TestMethod]
    public void Join_AnEmptyOrUnassignedElement_IsAZeroLengthString()
    {
        Assert.AreEqual("a  b", TextOf(Module().Join(VariantArray(new VBStringValue("a"), VBEmptyValue.Empty, new VBStringValue("b")))));
        Assert.AreEqual("a  b", TextOf(Module().Join(VariantArray(new VBStringValue("a"), null, new VBStringValue("b")))));
    }

    [TestMethod]
    public void Join_ANullElement_IsError94()
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().Join(VariantArray(new VBStringValue("a"), VBNullValue.Null, new VBStringValue("b")))));

    [TestMethod]
    public void Join_AnElementWithNoStringForm_IsError13()
    {
        // a nested Variant() has no String form; a nested Byte() would be read as its String (MS-VBAL 5.5.1.2.6).
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Join(VariantArray(new VBStringValue("a"), StringArray("b").TypedValue))));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Join(VariantArray(new VBStringValue("a"), new VBErrorValue(5)))));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Join(VariantArray(new VBStringValue("a"), VBMissingValue.Missing))));
        // an object is its default member's value, which an object of no class has none of (DefaultMemberTests).
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, ErrorOf(Module().Join(VariantArray(new VBStringValue("a"), new VBObjectValue(new VBRuntimeObjectId())))));
    }

    [TestMethod]
    public void Join_ANothingElement_IsError91()
    {
        // Nothing, Let-coerced to a String, is error 91 (MS-VBAL 5.5.1.2.13); an Object() element never assigned is Nothing, not Empty.
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, ErrorOf(Module().Join(VariantArray(new VBStringValue("a"), VBObjectValue.Nothing))));
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, ErrorOf(Module().Join(V(new VBResizableArrayValue([(0, 0)], VBObjectType.TypeInfo)))));
    }

    [TestMethod]
    public void Join_ANestedByteArray_IsItsString()
    {
        var bytes = new VBResizableByteArrayValue([(0, 1)]);
        bytes.TrySetElement(new RDCore.SDK.Model.Values.Bindings.ValueBindingHandle(new VBByteValue(0x41).RuntimeValue), 0);

        Assert.AreEqual("a A", TextOf(Module().Join(VariantArray(new VBStringValue("a"), bytes))));
    }

    [TestMethod]
    public void Join_OfAnEmptyArray_IsAZeroLengthString()
        => Assert.AreEqual(string.Empty, TextOf(Module().Join(StringArray())));

    [TestMethod]
    public void Join_OfAFixedSizeArray_TakesItsElementsInIndexOrder()
    {
        Assert.AreEqual("a b c", TextOf(Module().Join(FixedStringArray(1, "a", "b", "c"))));
        Assert.AreEqual("a,b,c", TextOf(Module().Join(FixedStringArray(1, "a", "b", "c"), V(","))));
    }

    [TestMethod]
    public void Join_OfAnythingButAOneDimensionalArray_IsError13()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Join(VNull)));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Join(VEmpty)));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Join(V("abc"))));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Join(V(new VBResizableArrayValue([(0, 1), (0, 1)], VBStringType.TypeInfo)))));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Join(V(new VBResizableArrayValue([], VBStringType.TypeInfo)))));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Join(V(VBResizableArrayValue.Empty))));
    }

    [TestMethod]
    public void Join_ChecksTheArrayBeforeTheDelimiter()
        // SourceArray's mismatch comes before the Delimiter's Null: 13, not 94.
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Join(VEmpty, VNull)));

    [TestMethod]
    public void Join_DropsAnElementsTrailingByte()
        => CollectionAssert.AreEqual(new byte[] { 0x41, 0x00 }, BytesOf(Module().Join(VariantArray(Bytes(0x41, 0x00, 0x42)), V(string.Empty))));

    [TestMethod]
    public void Join_FromSource_TakesSplitsStringArray()
        => CollectionAssert.AreEqual(new[] { "a-b-c", "[]" }, Run("Debug.Print Join(Split(\"a b c\"), \"-\")", "Debug.Print \"[\" & Join(Split(\"\")) & \"]\""));

    [TestMethod]
    public void Join_FromSource_TakesAVariantArrayAsWellAsAStringArray()
        => CollectionAssert.AreEqual(new[] { "a 1", "x,y" }, Run("Debug.Print Join(Array(\"a\", 1))", "Debug.Print Join(Split(\"x y\"), \",\")"));

    [TestMethod]
    public void Join_FromSource_OfAStringArrayAfterReDimAndErase()
    {
        var parts = Field("Parts", new VBResizableArrayType(VBStringType.TypeInfo));

        CollectionAssert.AreEqual(new[] { "a|b|", ",," }, RunWith([parts],
            "Parts = Split(\"a b\")", "ReDim Preserve Parts(0 To 2)", "Debug.Print Join(Parts, \"|\")",
            "ReDim Parts(0 To 2)", "Debug.Print Join(Parts, \",\")"));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, RunToErrorWith([parts], "Parts = Split(\"a b\")", "Erase Parts", "Debug.Print Join(Parts)"));
    }

    [TestMethod]
    public void Join_FromSource_OfNull_IsError13()
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, RunToError("V = Null", "Debug.Print Join(V)"));

    [TestMethod]
    public async Task Join_FromSource_ACallersMissingDelimiter_IsASpace()
    {
        // an omitted Optional Variant is Missing, and passed on to Join's Delimiter it is the Delimiter left out; an Empty one is "".
        var printed = await RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Private V As Variant\r\n" +
            "Private Function JoinWith(Optional d As Variant) As String\r\n    JoinWith = Join(Split(\"a b\"), d)\r\nEnd Function\r\n" +
            "Public Sub Main()\r\n    Debug.Print JoinWith()\r\n    Debug.Print JoinWith(V)\r\nEnd Sub\r\n");

        CollectionAssert.AreEqual(new[] { "a b", "ab" }, printed, string.Join(" | ", printed));
    }
}
