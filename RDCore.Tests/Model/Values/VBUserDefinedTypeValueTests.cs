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
    public void Size_SumsTheDeclaredSizeOfEachField()
    {
        var udt = Udt("Point", Field("X", VBLongType.TypeInfo), Field("Y", VBLongType.TypeInfo));

        var size = new VBUserDefinedTypeValue(udt).Size;

        Assert.AreEqual(VBLongType.TypeInfo.DefaultValue.Size * 2, size);
    }

    [TestMethod]
    public void Size_MixedFieldTypes_SumsEachFieldsOwnSize()
    {
        var udt = Udt("Mixed", Field("B", VBByteType.TypeInfo), Field("L", VBLongType.TypeInfo));

        var size = new VBUserDefinedTypeValue(udt).Size;

        Assert.AreEqual(VBByteType.TypeInfo.DefaultValue.Size + VBLongType.TypeInfo.DefaultValue.Size, size);
    }

    [TestMethod]
    public void Size_OfAnEmptyType_IsZero()
    {
        var udt = Udt("Empty");

        Assert.AreEqual(0, new VBUserDefinedTypeValue(udt).Size);
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
