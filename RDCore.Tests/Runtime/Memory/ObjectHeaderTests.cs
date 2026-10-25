using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Runtime.Memory;

/// <summary>
/// The interfaces of objects (<see cref="ObjectHeader"/>) and the <c>VARIANT</c>s of the session's memory, as MS-VBA lays them out (recorded in Excel, oracle\s17
/// O03-O08, V16, V22): an object variable's bytes are the address of an interface of its object, whose first pointer is its class's; a <c>Variant</c>'s are
/// its type and its data.
/// </summary>
[TestClass]
public sealed class ObjectHeaderTests
{
    private static SessionStorage Storage(Dictionary<VBRuntimeObjectId, string> classes)
        => new(new SessionMemory(new(), PointerSize.x64, SessionMemory.FirstAddress), pointerSize: 8) { ClassOf = held => classes.GetValueOrDefault(held) };

    private static MemoryAddress ObjectVariable(SessionStorage storage, VBRuntimeObjectId held, out ValueBindingHandle cell)
    {
        cell = new ValueBindingHandle(new VBRuntimeValue<VBRuntimeObjectId>(held));
        Assert.IsTrue(storage.TryAllocate(8, cell, out var address));
        storage.HoldsObject(address, VBObjectType.TypeInfo);
        return address;
    }

    private static MemoryAddress Variant(SessionStorage storage, VBTypedValue held, out ValueBindingHandle cell)
    {
        cell = new ValueBindingHandle(new VBRuntimeVariantValue(held.TypeInfo.VarType(), held));
        Assert.IsTrue(storage.TryAllocate(0, cell, out var address));
        return address;
    }

    private static long PointerOf(SessionStorage storage, MemoryAddress holder)
    {
        Assert.IsTrue(storage.TryGetObjectPointer(holder, out var pointer));
        return pointer;
    }

    private static byte[] Peek(SessionStorage storage, long at, int count)
    {
        var bytes = new byte[count];
        for (var index = 0; index < count; index++)
        {
            Assert.IsTrue(storage.TryPeek(new MemoryAddress((int)at + index), out bytes[index]), $"byte {index} at {at}");
        }

        return bytes;
    }

    private static void Poke(SessionStorage storage, MemoryAddress at, byte[] bytes)
    {
        for (var index = 0; index < bytes.Length; index++)
        {
            Assert.IsTrue(storage.TryPoke(new MemoryAddress(at.Value + index), bytes[index]), $"byte {index}");
        }
    }

    // O01, O03: Nothing is 0; an object variable's bytes are the address of its interface, the same every time.
    [TestMethod]
    public void AnObjectVariablesBytes_AreTheAddressOfItsInterface()
    {
        var thing = new VBRuntimeObjectId();
        var storage = Storage(new() { [thing] = "Thing" });
        var address = ObjectVariable(storage, thing, out _);
        var nothing = ObjectVariable(storage, VBObjectValue.Nothing.Value, out _);

        var pointer = PointerOf(storage, address);

        Assert.AreNotEqual(0, pointer);
        Assert.AreEqual(pointer, PointerOf(storage, address));
        Assert.AreEqual(pointer, BitConverter.ToInt64(Peek(storage, address.Value, 8)));
        Assert.AreEqual(0, PointerOf(storage, nothing));
    }

    // O05, O07, O08: an interface's first pointer is its class's, and what is past it reads as zeros.
    [TestMethod]
    public void AnInterfacesFirstPointer_IsItsClasss()
    {
        var (first, second, other) = (new VBRuntimeObjectId(), new VBRuntimeObjectId(), new VBRuntimeObjectId());
        var storage = Storage(new() { [first] = "Thing", [second] = "Thing", [other] = "Collection" });
        long TableOf(VBRuntimeObjectId held) => BitConverter.ToInt64(Peek(storage, PointerOf(storage, ObjectVariable(storage, held, out _)), 8));

        Assert.AreEqual(TableOf(first), TableOf(second), "two objects of a class");
        Assert.AreNotEqual(TableOf(first), TableOf(other), "objects of two classes");
        Assert.AreEqual(0, BitConverter.ToInt32(Peek(storage, PointerOf(storage, ObjectVariable(storage, first, out _)) + 8, 4)));
    }

    [TestMethod]
    public void AnInterface_IsNotWrittenByAPoke()
    {
        var thing = new VBRuntimeObjectId();
        var storage = Storage(new() { [thing] = "Thing" });

        Assert.IsFalse(storage.TryPoke(new MemoryAddress((int)PointerOf(storage, ObjectVariable(storage, thing, out _))), 1));
    }

    // O04: the address of an interface poked into an object variable makes it that object; 0 makes it Nothing.
    [TestMethod]
    public void AnInterfacesAddressPokedIntoAnObjectVariable_MakesItThatObject()
    {
        var thing = new VBRuntimeObjectId();
        var storage = Storage(new() { [thing] = "Thing" });
        var pointer = PointerOf(storage, ObjectVariable(storage, thing, out _));
        var weak = ObjectVariable(storage, VBObjectValue.Nothing.Value, out var cell);

        Poke(storage, weak, BitConverter.GetBytes(pointer));
        Assert.AreEqual(thing, ((VBRuntimeValue<VBRuntimeObjectId>)cell.Value).StoredValue);

        Poke(storage, weak, BitConverter.GetBytes(0L));
        Assert.AreEqual(VBObjectValue.Nothing.Value, ((VBRuntimeValue<VBRuntimeObjectId>)cell.Value).StoredValue);
    }

    [TestMethod]
    public void AnObjectThatIsDestroyed_LetsGoOfItsInterfaces()
    {
        var thing = new VBRuntimeObjectId();
        var storage = Storage(new() { [thing] = "Thing" });
        var pointer = PointerOf(storage, ObjectVariable(storage, thing, out _));

        storage.ReleaseHeadersOf(thing);

        Assert.IsFalse(storage.TryPeek(new MemoryAddress((int)pointer), out _));
    }

    // V16: a Variant's type is its first two bytes, its data eight bytes in.
    [TestMethod]
    [DataRow(3, 1234, DisplayName = "V16 a Long")]
    public void AVariantsBytes_AreItsTypeAndItsData(int type, int data)
    {
        var storage = Storage([]);
        var address = Variant(storage, new VBLongValue(data), out _);

        Assert.AreEqual(type, BitConverter.ToInt16(Peek(storage, address.Value, 2)));
        Assert.AreEqual(data, BitConverter.ToInt32(Peek(storage, address.Value + 8, 4)));
    }

    // V16, O06: a String's data is the address of its characters, an object's of its interface.
    [TestMethod]
    public void AVariantsPointers_AreTheAddressesOfWhatItHolds()
    {
        var thing = new VBRuntimeObjectId();
        var storage = Storage(new() { [thing] = "Thing" });
        var text = Variant(storage, new VBStringValue("x"), out _);
        var held = Variant(storage, new VBObjectValue(thing), out _);

        Assert.AreEqual(8, BitConverter.ToInt16(Peek(storage, text.Value, 2)));
        Assert.IsTrue(storage.TryGetStringPointer(text, out var characters));
        Assert.AreEqual(characters, BitConverter.ToInt64(Peek(storage, text.Value + 8, 8)));
        Assert.AreEqual(9, BitConverter.ToInt16(Peek(storage, held.Value, 2)));
        Assert.AreEqual(PointerOf(storage, ObjectVariable(storage, thing, out _)), BitConverter.ToInt64(Peek(storage, held.Value + 8, 8)));
    }

    // V22: an array's type is its element's and vbArray; Nothing is an object, Empty and Null are their own.
    [TestMethod]
    public void AVariantsType_IsItsVarType()
    {
        var storage = Storage([]);
        var array = Variant(storage, new VBFixedSizeArrayValue([(0, 2)], VBLongType.TypeInfo), out _);
        var nothing = Variant(storage, VBObjectValue.Nothing, out _);
        var empty = Variant(storage, VBEmptyValue.Empty, out _);
        var none = Variant(storage, VBNullValue.Null, out _);

        Assert.AreEqual(8195, BitConverter.ToInt16(Peek(storage, array.Value, 2)));
        Assert.AreEqual(9, BitConverter.ToInt16(Peek(storage, nothing.Value, 2)));
        Assert.AreEqual(0, BitConverter.ToInt16(Peek(storage, empty.Value, 2)));
        Assert.AreEqual(1, BitConverter.ToInt16(Peek(storage, none.Value, 2)));
    }

    [TestMethod]
    public void ADecimal_IsADecimalOverTheVariant()
    {
        var storage = Storage([]);
        var address = Variant(storage, new VBDecimalValue(-1.5m), out _);
        var image = Peek(storage, address.Value, 16);

        Assert.AreEqual(14, BitConverter.ToInt16(image, 0));
        Assert.AreEqual(1, image[2], "the scale");
        Assert.AreEqual(0x80, image[3], "the sign");
        Assert.AreEqual(15, BitConverter.ToInt64(image, 8));
    }

    // a POKE into a Variant's data is the value of its type the data is now; into its type, the data read as that type.
    [TestMethod]
    public void APokeIntoAVariant_IsWhatItsTypeAndDataSay()
    {
        var storage = Storage([]);
        var address = Variant(storage, new VBLongValue(1), out var cell);

        Assert.IsTrue(storage.TryPoke(new MemoryAddress(address.Value + 8), 7));
        Assert.AreEqual(7, ((VBRuntimeVariantValue)cell.Value).WrappedValue.RuntimeValue.BoxedValue);

        Assert.IsTrue(storage.TryPoke(address, 2));
        Assert.AreEqual(VBVarType.VBInteger, ((VBRuntimeVariantValue)cell.Value).ValueType);
        Assert.AreEqual((short)7, ((VBRuntimeVariantValue)cell.Value).WrappedValue.RuntimeValue.BoxedValue);

        Assert.IsFalse(storage.TryPoke(address, 8), "a String's data is a pointer to no String the session has");
    }

    // a Variant's String data is the pointer a String is: the address of another's characters makes it that String.
    [TestMethod]
    public void AnotherStringsCharactersPokedIntoAVariant_MakeItThatString()
    {
        var storage = Storage([]);
        var source = Variant(storage, new VBStringValue("hey"), out _);
        var target = Variant(storage, new VBStringValue("no"), out var cell);
        Assert.IsTrue(storage.TryGetStringPointer(source, out var characters));

        Poke(storage, new MemoryAddress(target.Value + 8), BitConverter.GetBytes(characters));

        Assert.AreEqual("hey", ((VBRuntimeVariantValue)cell.Value).WrappedValue.RuntimeValue.BoxedValue);
    }
}
