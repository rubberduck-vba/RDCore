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
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Runtime.Memory;

/// <summary>
/// The elements of an array laid out side by side in the session's memory (<see cref="ArrayBlock"/>), as MS-VBA lays them out (recorded in Excel, oracle\s17
/// V02-V12): what <c>VarPtr</c> of an element is, and the bytes a <c>PEEK</c> reads and a <c>POKE</c> writes there.
/// </summary>
[TestClass]
public sealed class ArrayBlockTests
{
    private static SessionStorage Storage() => new(new SessionMemory(new(), PointerSize.x64, SessionMemory.FirstAddress), pointerSize: 8);

    private static VBFixedSizeArrayValue Longs(params int[] values)
    {
        var array = new VBFixedSizeArrayValue([(0, values.Length - 1)], VBLongType.TypeInfo);
        for (var index = 0; index < values.Length; index++)
        {
            array.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<int>(values[index])), index);
        }

        return array;
    }

    private static VBUserDefinedType Pair()
    {
        var uri = TestUri.TestModuleUserDefinedTypeUri("TPair");
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, "TPair", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBUserDefinedType(symbol, [Field("X", VBLongType.TypeInfo), Field("Y", VBDoubleType.TypeInfo)]);
    }

    private static VBUserDefinedTypeFieldSymbol Field(string name, VBType type)
    {
        var uri = TestUri.TestUserDefinedTypeMemberUri(name);
        return new VBUserDefinedTypeFieldSymbol(uri, uri, name, type, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
    }

    private static int AddressOf(SessionStorage storage, VBArrayValue array, int index)
    {
        Assert.IsTrue(storage.TryAddressOfElement(array, index, out var address));
        return address.Value;
    }

    [TestMethod]
    [DataRow("Byte", 1)]
    [DataRow("Integer", 2)]
    [DataRow("Boolean", 2)]
    [DataRow("Long", 4)]
    [DataRow("Double", 8)]
    [DataRow("Currency", 8)]
    [DataRow("String", 8)]
    [DataRow("Variant", 24)]
    [DataRow("Object", 8)]
    public void TheElements_AreAsFarApartAsAnElementIsWide(string typeName, int width)
    {
        Assert.IsTrue(IntrinsicVBTypes.TryResolve(typeName, out var type));
        var storage = Storage();
        var array = new VBFixedSizeArrayValue([(0, 3)], type!);

        Assert.AreEqual(width, AddressOf(storage, array, 1) - AddressOf(storage, array, 0));
        Assert.AreEqual(3 * width, AddressOf(storage, array, 3) - AddressOf(storage, array, 0));
    }

    [TestMethod]
    public void AnElementsAddress_IsTheSameEveryTime_AndIsNotZero()
    {
        var storage = Storage();
        var array = Longs(1, 2, 3);

        Assert.AreEqual(AddressOf(storage, array, 1), AddressOf(storage, array, 1));
        Assert.AreNotEqual(0, AddressOf(storage, array, 0));
    }

    [TestMethod]
    public void AnotherArray_IsAnotherBlock()
    {
        var storage = Storage();

        Assert.AreNotEqual(AddressOf(storage, Longs(1, 2), 0), AddressOf(storage, Longs(1, 2), 0));
    }

    [TestMethod]
    public void ThereIsNoElementPastTheLast()
        => Assert.IsFalse(Storage().TryAddressOfElement(Longs(1, 2), 2, out _));

    [TestMethod]
    public void APeek_ReadsTheBytesOfTheElementsSideBySide()
    {
        var storage = Storage();
        var array = Longs(1000, 2);
        var start = new MemoryAddress(AddressOf(storage, array, 0));

        Assert.IsTrue(storage.TryPeek(start, out var low));
        Assert.IsTrue(storage.TryPeek(new MemoryAddress(start.Value + 1), out var high));
        Assert.IsTrue(storage.TryPeek(new MemoryAddress(start.Value + 4), out var next));

        Assert.AreEqual(1000, low | (high << 8), "the first element, little-endian");
        Assert.AreEqual(2, next, "the second element, four bytes on");
    }

    // V17, for an element: a POKE writes one byte, and the element is what its bytes spell.
    [TestMethod]
    public void APoke_WritesTheElementItFallsIn()
    {
        var storage = Storage();
        var array = Longs(1000, 2);

        Assert.IsTrue(storage.TryPoke(new MemoryAddress(AddressOf(storage, array, 0)), 42));

        Assert.AreEqual(810, ((VBLongValue)array.ElementAt(0)!).Value);
        Assert.AreEqual(2, ((VBLongValue)array.ElementAt(1)!).Value, "the other element is as it was");
    }

    [TestMethod]
    public void AnElementsAddress_ReadsItsCell()
    {
        var storage = Storage();
        var array = Longs(5, 6);

        Assert.IsTrue(storage.TryRead(new MemoryAddress(AddressOf(storage, array, 1)), out var cell));
        Assert.AreSame(array.CellAt(1), cell);
    }

    [TestMethod]
    public void TheElementsOfARecordArray_AreRecordsAtTheirOffsets()
    {
        var storage = Storage();
        var pair = Pair();
        var array = new VBFixedSizeArrayValue([(0, 1)], pair);
        var second = (VBUserDefinedTypeValue)array.ElementAt(1)!;
        second.TrySetFieldAt(0, new VBLongValue(7));

        var width = AddressOf(storage, array, 1) - AddressOf(storage, array, 0);
        Assert.IsTrue(storage.TryPeek(new MemoryAddress(AddressOf(storage, array, 1)), out var x));

        Assert.AreEqual(16, width, "a Long, padded to the Double after it");
        Assert.AreEqual(7, x);
    }

    [TestMethod]
    public void APokeIntoARecordElement_WritesItsField()
    {
        var storage = Storage();
        var array = new VBFixedSizeArrayValue([(0, 1)], Pair());

        Assert.IsTrue(storage.TryPoke(new MemoryAddress(AddressOf(storage, array, 1)), 9));

        Assert.AreEqual(9, ((VBLongValue)((VBUserDefinedTypeValue)array.ElementAt(1)!).FieldAt(0)!).Value);
    }

    // V25: a record variable's bytes are its fields at their offsets, and a POKE writes the field it falls in.
    [TestMethod]
    public void ARecordVariable_IsItsFieldsBytes()
    {
        var storage = Storage();
        var record = new VBUserDefinedTypeValue(Pair());
        record.TrySetFieldAt(0, new VBLongValue(77));
        var handle = new ValueBindingHandle(new VBRuntimeValue<VBRuntimeUserDefinedTypeValue>(new VBRuntimeUserDefinedTypeValue(record)));
        Assert.IsTrue(storage.TryAllocate(record.Size, handle, out var address));

        Assert.IsTrue(storage.TryPeek(address, out var x));
        Assert.IsTrue(storage.TryPoke(address, 1));

        Assert.AreEqual(77, x);
        Assert.AreEqual(1, ((VBLongValue)record.FieldAt(0)!).Value);
    }
}
