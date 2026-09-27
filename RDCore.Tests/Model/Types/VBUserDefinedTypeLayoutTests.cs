using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Tests.Model.Types;

/// <summary>
/// <strong>MS-VBAL §2.1</strong>'s "linear concatenation of the aggregated data values possibly with
/// implementation defined padding between data values", as <see cref="VBUserDefinedTypeLayout"/> lays it out.
/// </summary>
/// <remarks>
/// This is the in-memory size — what <c>LenB</c> reports of a UDT (<strong>§6.1.2.11</strong>). The other
/// size, "as it will be written to the file", has no padding at all and belongs to the record format; these
/// pin the padding so that a change to it has to be deliberate.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 2.1 UDT layout")]
public sealed class VBUserDefinedTypeLayoutTests
{
    private static VBUserDefinedType Udt(string name, params (string Name, VBType Type)[] fields)
    {
        var uri = TestUri.TestModuleUserDefinedTypeUri(name);
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, name, ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBUserDefinedType(symbol, [.. fields.Select(field => Field(name, field.Name, field.Type))]);
    }

    private static VBUserDefinedTypeFieldSymbol Field(string typeName, string name, VBType type)
    {
        var uri = TestUri.TestUserDefinedTypeMemberUri(name, typeName);
        return new VBUserDefinedTypeFieldSymbol(uri, uri, name, type, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
    }

    [TestMethod]
    public void AnEmptyType_HasNoSize()
    {
        var layout = VBUserDefinedTypeLayout.Of(Udt("Empty"));

        Assert.AreEqual(0, layout.Size);
        Assert.IsEmpty(layout.Fields);
    }

    [TestMethod]
    public void FieldsOfEqualWidth_SitBackToBack()
    {
        var layout = VBUserDefinedTypeLayout.Of(Udt("Point", ("X", VBLongType.TypeInfo), ("Y", VBLongType.TypeInfo)));

        Assert.AreSequenceEqual<int>([0, 4], layout.Fields.Select(field => field.Offset));
        Assert.AreEqual(8, layout.Size);
    }

    [TestMethod]
    public void AByteBeforeALong_PadsTheLongOntoItsOwnBoundary()
    {
        // the classic case: 5 bytes of data in 8 bytes of memory, the Long aligned to 4.
        var layout = VBUserDefinedTypeLayout.Of(Udt("Mixed", ("B", VBByteType.TypeInfo), ("L", VBLongType.TypeInfo)));

        Assert.AreSequenceEqual<int>([0, 4], layout.Fields.Select(field => field.Offset));
        Assert.AreEqual(8, layout.Size);
    }

    [TestMethod]
    public void AnIntegerBeforeALong_PadsByTwo()
    {
        var layout = VBUserDefinedTypeLayout.Of(Udt("Mixed", ("I", VBIntegerType.TypeInfo), ("L", VBLongType.TypeInfo)));

        Assert.AreSequenceEqual<int>([0, 4], layout.Fields.Select(field => field.Offset));
        Assert.AreEqual(8, layout.Size);
    }

    [TestMethod]
    public void TwoIntegers_NeedNoPadding()
    {
        var layout = VBUserDefinedTypeLayout.Of(Udt("Pair", ("A", VBIntegerType.TypeInfo), ("B", VBIntegerType.TypeInfo)));

        Assert.AreSequenceEqual<int>([0, 2], layout.Fields.Select(field => field.Offset));
        Assert.AreEqual(4, layout.Size);
    }

    [TestMethod]
    public void ADouble_IsAlignedToEight()
    {
        var layout = VBUserDefinedTypeLayout.Of(Udt("Mixed", ("B", VBByteType.TypeInfo), ("D", VBDoubleType.TypeInfo)));

        Assert.AreSequenceEqual<int>([0, 8], layout.Fields.Select(field => field.Offset));
        Assert.AreEqual(16, layout.Size);
    }

    [TestMethod]
    public void ATypeIsPaddedUpToItsOwnAlignment()
    {
        // trailing padding, so that an array of the type keeps every element on the boundary its fields need.
        var layout = VBUserDefinedTypeLayout.Of(Udt("Mixed", ("L", VBLongType.TypeInfo), ("B", VBByteType.TypeInfo)));

        Assert.AreEqual(4, layout.Alignment);
        Assert.AreEqual(8, layout.Size, "5 bytes of data, rounded up to the Long's own boundary");
    }

    [TestMethod]
    public void AVariableLengthStringField_IsAPointer()
    {
        // a String field holds a pointer to its characters, never the characters - which is exactly why Len
        // "might not be able to determine the actual number of storage bytes required when used with
        // variable-length strings in user-defined data types" (MS-VBAL 6.1.2.11).
        var layout = VBUserDefinedTypeLayout.Of(Udt("Named", ("S", VBStringType.TypeInfo)));

        Assert.AreEqual(VBUserDefinedTypeLayout.DefaultPointerWidth, layout.Size);
    }

    [TestMethod]
    public void AFixedLengthStringField_IsItsCharactersInline_InUnicode()
    {
        // and Unicode in memory, where a record gets the ANSI bytes - the plainest case of Len and LenB
        // disagreeing about a UDT.
        var layout = VBUserDefinedTypeLayout.Of(Udt("Named", ("S", new VBFixedStringType(4))));

        Assert.AreEqual(8, layout.Size);
        Assert.AreEqual(1, layout.Alignment, "characters need no alignment of their own");
    }

    [TestMethod]
    public void ANestedType_ContributesItsOwnPaddedSize()
    {
        var inner = Udt("Inner", ("B", VBByteType.TypeInfo), ("L", VBLongType.TypeInfo));
        var layout = VBUserDefinedTypeLayout.Of(Udt("Outer", ("N", inner), ("B", VBByteType.TypeInfo)));

        Assert.AreSequenceEqual<int>([0, 8], layout.Fields.Select(field => field.Offset));
        Assert.AreEqual(12, layout.Size, "the nested type is 8, then a Byte, rounded up to the nested type's own 4");
    }

    [TestMethod]
    public void ATypeThatNestsItself_HasAFiniteSize()
    {
        // VBA forbids it and nothing in this model does, so measuring it must terminate rather than recur until
        // the stack runs out - the same hazard the type's own Equals override exists for.
        var recursive = Udt("Recursive", ("L", VBLongType.TypeInfo));
        var looped = (VBUserDefinedType)recursive.WithMembers(
            [.. recursive.Members, Field("Recursive", "Self", recursive)]);

        Assert.AreEqual(4, VBUserDefinedTypeLayout.Of(looped).Size);
    }

    [TestMethod]
    public void OffsetOf_FindsAFieldCaseInsensitively()
    {
        var layout = VBUserDefinedTypeLayout.Of(Udt("Point", ("X", VBLongType.TypeInfo), ("Y", VBLongType.TypeInfo)));

        Assert.AreEqual(4, layout.OffsetOf("y"));
    }

    [TestMethod]
    public void OffsetOf_AFieldTheTypeDoesNotDeclare_IsNull()
        => Assert.IsNull(VBUserDefinedTypeLayout.Of(Udt("Point", ("X", VBLongType.TypeInfo))).OffsetOf("Z"));

    [TestMethod]
    public void AWiderPointer_WidensAPointerField()
    {
        // what a 64-bit host would lay the same declaration out as, which is why the width is a parameter
        // rather than a constant in the measuring code.
        var layout = VBUserDefinedTypeLayout.Of(Udt("Named", ("S", VBStringType.TypeInfo)), pointerWidth: 8);

        Assert.AreEqual(8, layout.Size);
    }
}
