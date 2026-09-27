using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Model.Values;

[TestClass]
public sealed class VBUserDefinedTypeValueTests
{
    private static VBUserDefinedType Udt(string name, params VBTypeMemberSymbol[] fields)
    {
        var uri = TestUri.TestModuleUserDefinedTypeUri(name);
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, name, ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBUserDefinedType(symbol, [.. fields]);
    }

    private static VBUserDefinedTypeFieldSymbol Field(string name, VBType type)
    {
        var uri = TestUri.TestUserDefinedTypeMemberUri(name);
        return new VBUserDefinedTypeFieldSymbol(uri, uri, name, type, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
    }

    [TestMethod]
    public void Size_FieldsThatNeedNoPadding_IsTheirTotalWidth()
    {
        var udt = Udt("Point", Field("X", VBLongType.TypeInfo), Field("Y", VBLongType.TypeInfo));

        Assert.AreEqual(8, new VBUserDefinedTypeValue(udt).Size);
    }

    [TestMethod]
    public void Size_IsTheInMemorySize_PaddingIncluded()
    {
        // MS-VBAL 6.1.2.11: "LenB returns the in-memory size, including any implementation-specific padding
        // between elements" - a Byte followed by a Long is 5 bytes of data in 8 bytes of memory, because the
        // Long is aligned to 4. The 5 is what Len reports, being "the size as it will be written to the file".
        var udt = Udt("Mixed", Field("B", VBByteType.TypeInfo), Field("L", VBLongType.TypeInfo));

        Assert.AreEqual(8, new VBUserDefinedTypeValue(udt).Size);
    }

    [TestMethod]
    public void Size_OfAnEmptyType_IsZero()
    {
        var udt = Udt("Empty");

        Assert.AreEqual(0, new VBUserDefinedTypeValue(udt).Size);
    }

    [TestMethod]
    public void Fields_AreTheDeclaredFields_InDeclarationOrder()
    {
        // the order a record is written and read in (MS-VBAL 5.4.5.11), so it is not incidental.
        var udt = Udt("Point", Field("X", VBLongType.TypeInfo), Field("Y", VBLongType.TypeInfo));

        var value = new VBUserDefinedTypeValue(udt);

        Assert.AreSequenceEqual<string>(["X", "Y"], value.Fields.Select(field => field.Name));
    }

    [TestMethod]
    public void Field_StartsAtItsDeclaredTypesDefault()
    {
        var udt = Udt("Point", Field("X", VBLongType.TypeInfo));

        var value = new VBUserDefinedTypeValue(udt);

        Assert.AreEqual(0, Convert.ToInt32(value["X"]!.Handle.Value.BoxedValue));
    }

    [TestMethod]
    public void Field_IsFoundCaseInsensitively()
        // VBA compares identifiers without regard to case, so a field is reachable however it is spelled.
        => Assert.IsNotNull(new VBUserDefinedTypeValue(Udt("Point", Field("X", VBLongType.TypeInfo)))["x"]);

    [TestMethod]
    public void Field_ThatTheTypeDoesNotDeclare_IsNull()
        => Assert.IsNull(new VBUserDefinedTypeValue(Udt("Point", Field("X", VBLongType.TypeInfo)))["Z"]);

    [TestMethod]
    public void TrySetField_ReplacesThatFieldAlone()
    {
        var udt = Udt("Point", Field("X", VBLongType.TypeInfo), Field("Y", VBLongType.TypeInfo));
        var value = new VBUserDefinedTypeValue(udt);

        Assert.IsTrue(value.TrySetField("X", new VBLongValue(7)));

        Assert.AreEqual(7, Convert.ToInt32(value["X"]!.Handle.Value.BoxedValue));
        Assert.AreEqual(0, Convert.ToInt32(value["Y"]!.Handle.Value.BoxedValue), "the other field is untouched");
    }

    [TestMethod]
    public void TrySetField_AFieldTheTypeDoesNotDeclare_ReturnsFalse()
        => Assert.IsFalse(new VBUserDefinedTypeValue(Udt("Point", Field("X", VBLongType.TypeInfo)))
            .TrySetField("Z", new VBLongValue(7)));

    [TestMethod]
    public void TwoValuesOfTheSameType_DoNotShareFieldStorage()
    {
        // a type's DefaultValue is a cached instance every caller starts from, and both binding kinds mutate in
        // place - so sharing it would let a write to one variable's field reach every other variable's.
        var udt = Udt("Point", Field("X", VBLongType.TypeInfo));
        var first = new VBUserDefinedTypeValue(udt);
        var second = new VBUserDefinedTypeValue(udt);

        first.TrySetField("X", new VBLongValue(7));

        Assert.AreEqual(0, Convert.ToInt32(second["X"]!.Handle.Value.BoxedValue));
    }

    [TestMethod]
    public void ACopy_HasFieldStorageOfItsOwn()
    {
        // VBA copies a UDT on assignment, so a `with`-derived copy must not alias the original's fields -
        // TryAllocateIn makes one of those on every allocation.
        var udt = Udt("Point", Field("X", VBLongType.TypeInfo));
        var original = new VBUserDefinedTypeValue(udt);

        var copy = original with { };
        copy.TrySetField("X", new VBLongValue(7));

        Assert.AreEqual(0, Convert.ToInt32(original["X"]!.Handle.Value.BoxedValue));
    }

    [TestMethod]
    public void ACopy_OfANestedUdt_IsDeep()
    {
        var inner = Udt("Inner", Field("N", VBLongType.TypeInfo));
        var outer = Udt("Outer", Field("Nested", inner));
        var original = new VBUserDefinedTypeValue(outer);

        var copy = original with { };
        ((VBUserDefinedTypeValue)copy["Nested"]!).TrySetField("N", new VBLongValue(7));

        Assert.AreEqual(0, Convert.ToInt32(((VBUserDefinedTypeValue)original["Nested"]!)["N"]!.Handle.Value.BoxedValue));
    }

    [TestMethod]
    public void CreateValue_FromAStoredBinding_HandsBackTheSameFields()
    {
        // the reason a UDT is boxed on the way into storage: rebuilding it from a scalar binding would hand
        // back a UDT with default fields however much had been assigned to it.
        var udt = Udt("Point", Field("X", VBLongType.TypeInfo));
        var value = new VBUserDefinedTypeValue(udt);
        value.TrySetField("X", new VBLongValue(7));

        var stored = new ValueBindingHandle(new VBRuntimeValue<VBRuntimeUserDefinedTypeValue>(new VBRuntimeUserDefinedTypeValue(value)));

        Assert.AreEqual(7, Convert.ToInt32(((VBUserDefinedTypeValue)udt.CreateValue(stored))["X"]!.Handle.Value.BoxedValue));
    }

    [TestMethod]
    public void TryAllocateIn_BindsTheAllocatedAddressAsItsOwnValue()
    {
        var udt = Udt("Point", Field("X", VBLongType.TypeInfo), Field("Y", VBLongType.TypeInfo));
        var value = new VBUserDefinedTypeValue(udt);
        var storage = new SessionStorage(new SessionMemory(new(), PointerSize.x86));

        Assert.IsTrue(value.TryAllocateIn(storage, out var allocated));

        Assert.IsTrue(storage.TryRead(allocated.Value, out var bound));
        Assert.AreSame(allocated.Handle, bound);
    }

    [TestMethod]
    public void TryAllocateIn_DistinctAllocations_AreNotEqual()
    {
        var udt = Udt("Point", Field("X", VBLongType.TypeInfo));
        var storage = new SessionStorage(new SessionMemory(new(), PointerSize.x86));

        new VBUserDefinedTypeValue(udt).TryAllocateIn(storage, out var first);
        new VBUserDefinedTypeValue(udt).TryAllocateIn(storage, out var second);

        Assert.IsFalse(first!.Equals(second));
    }

    [TestMethod]
    public void TryAllocateIn_OutOfMemory_ReturnsFalse()
    {
        var udt = Udt("Point", Field("X", VBLongType.TypeInfo));
        var value = new VBUserDefinedTypeValue(udt);
        var storage = Substitute.For<ISessionStorage>();
        storage.TryAllocate(Arg.Any<int>(), Arg.Any<IBindingHandle>(), out Arg.Any<MemoryAddress>()).Returns(false);

        Assert.IsFalse(value.TryAllocateIn(storage, out var allocated));
        Assert.IsNull(allocated);
    }
}
