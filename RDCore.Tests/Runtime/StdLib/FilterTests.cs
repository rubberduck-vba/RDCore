using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.7 Filter</strong>: "a zero-based array containing subset of a string array based on a specified
/// filter criteria" - the elements that contain Match as a substring, or with Include False the ones that do not.
/// </summary>
/// <remarks>
/// The array's bounds and element type are asserted in C#; from source, it is joined.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.7 Filter")]
public sealed class FilterTests
{
    private const VBCompareMethod Binary = VBCompareMethod.VBBinaryCompare;
    private const VBCompareMethod Text = VBCompareMethod.VBTextCompare;

    private static readonly VBBooleanValue Include = new(true);
    private static readonly VBBooleanValue Exclude = new(false);

    [TestMethod]
    public void Filter_KeepsTheElementsThatContainTheMatch_OrThoseThatDoNot()
    {
        CollectionAssert.AreEqual(new[] { "ab" }, ElementsOf(Module().Filter(StringArray("ab", "b", "AB"), new VBStringValue("a"), Include, Binary)));
        CollectionAssert.AreEqual(new[] { "b", "AB" }, ElementsOf(Module().Filter(StringArray("ab", "b", "AB"), new VBStringValue("a"), Exclude, Binary)));
    }

    [TestMethod]
    public void Filter_InTextMode_IgnoresCase()
        => CollectionAssert.AreEqual(new[] { "ab", "AB" }, ElementsOf(Module().Filter(StringArray("ab", "b", "AB"), new VBStringValue("a"), Include, Text)));

    [TestMethod]
    public void Filter_WithNoMatch_IsAnEmptyZeroBasedStringArray()
    {
        var result = Module().Filter(StringArray("a"), new VBStringValue("x"), Include, Binary);

        Assert.IsEmpty(ElementsOf(result));
        Assert.AreEqual((0, -1), (ArrayOf(result).Dimensions[0].LowerBound, ArrayOf(result).Dimensions[0].UpperBound));
    }

    [TestMethod]
    public void Filter_OfAVariantArray_TakesEachElementsString()
        => CollectionAssert.AreEqual(new[] { "1", "a1", "12" },
            ElementsOf(Module().Filter(VariantArray(new VBLongValue(1), new VBStringValue("a1"), new VBLongValue(12), new VBLongValue(3)), new VBStringValue("1"), Include, Binary)));

    [TestMethod]
    public void Filter_AZeroLengthMatch_IsInEveryElementButAZeroLengthOne()
    {
        CollectionAssert.AreEqual(new[] { "a", "b" }, ElementsOf(Module().Filter(StringArray("a", "", "b"), new VBStringValue(string.Empty), Include, Binary)));
        CollectionAssert.AreEqual(new[] { "" }, ElementsOf(Module().Filter(StringArray("a", "", "b"), new VBStringValue(string.Empty), Exclude, Binary)));
    }

    [TestMethod]
    public void Filter_AnElementNeverAssigned_IsAZeroLengthString()
    {
        CollectionAssert.AreEqual(new[] { "a" }, ElementsOf(Module().Filter(VariantArray(new VBStringValue("a"), null), new VBStringValue(string.Empty), Include, Binary)));
        CollectionAssert.AreEqual(new[] { "" }, ElementsOf(Module().Filter(VariantArray(new VBStringValue("a"), null), new VBStringValue("a"), Exclude, Binary)));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Filter_ANullElement_IsError94_WhetherItWouldBeKeptOrNot(bool include)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull,
            ErrorOf(Module().Filter(VariantArray(new VBStringValue("a"), VBNullValue.Null, new VBStringValue("ab")), new VBStringValue("a"), new VBBooleanValue(include), Binary)));

    [TestMethod]
    public void Filter_OfAFixedSizeArray_IsStillZeroBased()
        => CollectionAssert.AreEqual(new[] { "b" }, ElementsOf(Module().Filter(FixedStringArray(1, "a", "b", "c"), new VBStringValue("b"), Include, Binary)));

    [TestMethod]
    public void Filter_OfAnythingButAOneDimensionalArray_IsError13()
    {
        foreach (var notOne in new[]
        {
            VNull, VEmpty, V("abc"),
            V(new VBResizableArrayValue([(0, 1), (0, 1)], VBStringType.TypeInfo)),
            V(new VBResizableArrayValue([], VBStringType.TypeInfo)),
            V(VBResizableArrayValue.Empty),
        })
        {
            Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Filter(notOne, new VBStringValue("a"), Include, Binary)));
        }
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(-1)]
    [DataRow(1033)]
    public void Filter_WithACompareOtherThanZeroOrOne_IsError5(int compare)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Filter(StringArray("a"), new VBStringValue("a"), Include, (VBCompareMethod)compare)));

    [TestMethod]
    public void Filter_KeepsAnElementAsItIs()
    {
        // a String that ends in a byte is selected, not rebuilt: the byte stays.
        var kept = ArrayOf(Module().Filter(VariantArray(Bytes(0x41, 0x00, 0x42)), new VBStringValue("A"), Include, Binary));

        Assert.AreEqual(3, ((VBStringValue)kept.ElementAt(0)!).ByteLength);
    }

    [TestMethod]
    public void Filter_FromSource_TakesSplitsArray_AndIncludesWhenIncludeIsLeftOut()
        => Assert.AreEqual("ab", Run("Debug.Print Join(Filter(Split(\"ab b AB\"), \"a\"), \"|\")").Single());

    [TestMethod]
    public void Filter_FromSource_IsZeroBased_WhateverTheOptionBase()
        => CollectionAssert.AreEqual(new[] { "0", "String()" }, RunIn(new RuntimeSourceHarness.Surroundings { Directives = new ModuleDirectives(Base: 1) }, [],
            "Debug.Print LBound(Filter(Array(\"a\", \"b\"), \"b\"))", "Debug.Print TypeName(Filter(Array(\"a\"), \"x\"))"));

    [TestMethod]
    public void Filter_FromSource_IncludeIsAnyBoolean()
        => CollectionAssert.AreEqual(new[] { "b", "a", "b" }, Run(
            "Debug.Print Join(Filter(Split(\"a b\"), \"a\", 0), \"|\")",
            "Debug.Print Join(Filter(Split(\"a b\"), \"a\", 5), \"|\")",
            "Debug.Print Join(Filter(Split(\"a b\"), \"a\", \"False\"), \"|\")"));

    [TestMethod]
    public void Filter_FromSource_UnderTheModulesOptionCompare()
    {
        Assert.AreEqual("A", RunUnder(OptionCompare.Text, "Debug.Print Join(Filter(Split(\"A b\"), \"a\"), \"|\")").Single());
        Assert.AreEqual("[]", Run("Debug.Print \"[\" & Join(Filter(Split(\"A b\"), \"a\"), \"|\") & \"]\"").Single());
        Assert.AreEqual("b", RunUnder(OptionCompare.Text, "Debug.Print Join(Filter(Split(\"A b\"), \"a\", False), \"|\")").Single());
    }

    [TestMethod]
    public void Filter_FromSource_OfNull_IsError13()
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, RunToError("V = Null", "Debug.Print Join(Filter(V, \"a\"))"));
}
