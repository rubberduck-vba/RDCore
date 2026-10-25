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
/// The <c>BSTR</c> a variable-length <c>String</c> is in the session's memory (<see cref="StringBlock"/>), as MS-VBA lays one out (recorded in Excel, oracle\s17
/// S04-S13): its length in bytes, its characters - where <c>StrPtr</c> points - and a null character; and a <c>String</c> variable's bytes, the address of them.
/// </summary>
[TestClass]
public sealed class StringBlockTests
{
    private static SessionStorage Storage() => new(new SessionMemory(new(), PointerSize.x64, SessionMemory.FirstAddress), pointerSize: 8);

    private static (SessionStorage Storage, ValueBindingHandle Cell, MemoryAddress Address) StringVariable(IRuntimeValue value)
    {
        var storage = Storage();
        var cell = new ValueBindingHandle(value);
        Assert.IsTrue(storage.TryAllocate(0, cell, out var address));
        storage.HoldsString(address);
        return (storage, cell, address);
    }

    private static long PointerOf(SessionStorage storage, MemoryAddress holder)
    {
        Assert.IsTrue(storage.TryGetStringPointer(holder, out var pointer));
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

    private static VBUserDefinedTypeFieldSymbol Field(string name, VBType type)
    {
        var uri = TestUri.TestUserDefinedTypeMemberUri(name);
        return new VBUserDefinedTypeFieldSymbol(uri, uri, name, type, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
    }

    private static VBUserDefinedType Named()
    {
        var uri = TestUri.TestModuleUserDefinedTypeUri("TNamed");
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, "TNamed", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBUserDefinedType(symbol, [Field("B", VBByteType.TypeInfo), Field("S", VBStringType.TypeInfo)]);
    }

    // S05: the length in bytes at -4, the characters, then a null character.
    [TestMethod]
    public void TheCharacters_AreBetweenTheirLengthAndANullCharacter()
    {
        var (storage, _, address) = StringVariable(new VBRuntimeValue<string>("abc"));
        var characters = PointerOf(storage, address);

        CollectionAssert.AreEqual(new byte[] { 6, 0, 0, 0 }, Peek(storage, characters - 4, 4));
        CollectionAssert.AreEqual(new byte[] { 97, 0, 98, 0, 99, 0, 0, 0 }, Peek(storage, characters, 8));
    }

    // S13: a String with an odd number of bytes counts them all.
    [TestMethod]
    public void AnOddByteLength_IsTheLength()
    {
        var (storage, _, address) = StringVariable(VBRuntimeByteStringValue.FromBytes([0x61, 0, 0x61, 0, 0x61, 0, 0x41]));

        CollectionAssert.AreEqual(new byte[] { 7, 0, 0, 0 }, Peek(storage, PointerOf(storage, address) - 4, 4));
    }

    // S04: a String variable's bytes are the address of its characters.
    [TestMethod]
    public void AStringVariablesBytes_AreTheAddressOfItsCharacters()
    {
        var (storage, _, address) = StringVariable(new VBRuntimeValue<string>("abc"));

        Assert.AreEqual(PointerOf(storage, address), BitConverter.ToInt64(Peek(storage, address.Value, 8)));
    }

    // S01: vbNullString points nowhere.
    [TestMethod]
    public void vbNullString_IsZero()
    {
        var (storage, _, address) = StringVariable(new VBRuntimeValue<string>(null!));

        Assert.AreEqual(0, PointerOf(storage, address));
        Assert.AreEqual(0, BitConverter.ToInt64(Peek(storage, address.Value, 8)));
    }

    // S02: the same until another String is assigned, which is somewhere else; the first is freed.
    [TestMethod]
    public void AnotherString_IsSomewhereElse_AndTheFirstIsFreed()
    {
        var (storage, cell, address) = StringVariable(new VBRuntimeValue<string>("abc"));
        var first = PointerOf(storage, address);
        Assert.AreEqual(first, PointerOf(storage, address));

        cell.Store(new VBRuntimeValue<string>("abc"), isKnown: true);
        var second = PointerOf(storage, address);

        Assert.AreNotEqual(first, second);
        Assert.IsFalse(storage.TryPeek(new MemoryAddress((int)first), out _), "the first BSTR is freed");
    }

    // S12: a POKE into the characters changes the String, in place.
    [TestMethod]
    public void APokeIntoTheCharacters_ChangesTheString_InPlace()
    {
        var (storage, cell, address) = StringVariable(new VBRuntimeValue<string>("abc"));
        var characters = PointerOf(storage, address);

        Assert.IsTrue(storage.TryPoke(new MemoryAddress((int)characters), 122));

        Assert.AreEqual("zbc", cell.Value.BoxedValue);
        Assert.AreEqual(characters, PointerOf(storage, address), "the characters are where they were");
    }

    private static void PokePointer(SessionStorage storage, MemoryAddress at, long pointer)
    {
        var bytes = BitConverter.GetBytes(pointer);
        for (var index = 0; index < bytes.Length; index++)
        {
            Assert.IsTrue(storage.TryPoke(new MemoryAddress(at.Value + index), bytes[index]), $"byte {index}");
        }
    }

    // grid-p3 L01: the length says how much of the data the String is, and the characters past it stay where they are.
    [TestMethod]
    public void APokeIntoTheLength_IsTheLengthOfTheString_AndTheCharactersPastItStay()
    {
        var (storage, cell, address) = StringVariable(new VBRuntimeValue<string>("abcdef"));
        var characters = PointerOf(storage, address);

        Assert.IsTrue(storage.TryPoke(new MemoryAddress((int)characters - 4), 4));
        Assert.AreEqual("ab", cell.Value.BoxedValue);
        Assert.AreEqual(characters, PointerOf(storage, address), "the characters are where they were");
        CollectionAssert.AreEqual(new byte[] { 99, 0 }, Peek(storage, characters + 4, 2), "the third character is still there");

        Assert.IsTrue(storage.TryPoke(new MemoryAddress((int)characters - 4), 12));
        Assert.AreEqual("abcdef", cell.Value.BoxedValue);
    }

    // grid-p3 L04: a length of 0 is a zero-length String, which is not vbNullString: its characters are where they were.
    [TestMethod]
    public void ALengthOfZero_IsAZeroLengthString()
    {
        var (storage, cell, address) = StringVariable(new VBRuntimeValue<string>("abc"));
        var characters = PointerOf(storage, address);

        Assert.IsTrue(storage.TryPoke(new MemoryAddress((int)characters - 4), 0));

        Assert.AreEqual(string.Empty, cell.Value.BoxedValue);
        Assert.AreEqual(characters, PointerOf(storage, address));
    }

    [TestMethod]
    public void ALengthPastTheBlock_IsRefused()
    {
        var (storage, cell, address) = StringVariable(new VBRuntimeValue<string>("abc"));

        Assert.IsFalse(storage.TryPoke(new MemoryAddress((int)PointerOf(storage, address) - 4), 200));
        Assert.AreEqual("abc", cell.Value.BoxedValue);
    }

    // grid-p3 Q01: a String given the address of another's characters holds the same String, and a change to the characters through either is the other's.
    [TestMethod]
    public void AStringGivenAnothersCharacters_HoldsTheSameString()
    {
        var storage = Storage();
        var s = new ValueBindingHandle(new VBRuntimeValue<string>("abc"));
        var t = new ValueBindingHandle(new VBRuntimeValue<string>(null!));
        Assert.IsTrue(storage.TryAllocate(0, s, out var sAddress));
        Assert.IsTrue(storage.TryAllocate(0, t, out var tAddress));
        storage.HoldsString(sAddress);
        storage.HoldsString(tAddress);
        var characters = PointerOf(storage, sAddress);

        PokePointer(storage, tAddress, characters);
        Assert.AreEqual("abc", t.Value.BoxedValue);
        Assert.AreEqual(characters, PointerOf(storage, tAddress));

        Assert.IsTrue(storage.TryPoke(new MemoryAddress((int)characters), 122));
        Assert.AreEqual("zbc", t.Value.BoxedValue, "through s");
        Assert.IsTrue(storage.TryPoke(new MemoryAddress((int)characters + 4), 113));
        Assert.AreEqual("zbq", s.Value.BoxedValue, "through t");

        PokePointer(storage, tAddress, 0);
        Assert.IsNull(t.Value.BoxedValue, "vbNullString");
        Assert.AreEqual("zbq", s.Value.BoxedValue);
        Assert.AreEqual(characters, PointerOf(storage, sAddress));
    }

    // grid-p3 Q01: the Mid statement through one is the other's too.
    [TestMethod]
    public void WritingInPlaceThroughOne_IsTheOthers()
    {
        var storage = Storage();
        var s = new ValueBindingHandle(new VBRuntimeValue<string>("abc"));
        var t = new ValueBindingHandle(new VBRuntimeValue<string>(null!));
        Assert.IsTrue(storage.TryAllocate(0, s, out var sAddress));
        Assert.IsTrue(storage.TryAllocate(0, t, out var tAddress));
        storage.HoldsString(tAddress);
        PokePointer(storage, tAddress, PointerOf(storage, sAddress));

        var block = storage.BlockOf(t.Value)!;
        t.Store(new VBRuntimeValue<string>("abq"), isKnown: true);
        storage.KeepInPlace(block);

        Assert.AreEqual("abq", s.Value.BoxedValue);
        Assert.AreEqual(PointerOf(storage, sAddress), PointerOf(storage, tAddress));
    }

    // grid-p3 Q03: a pointer of 0 is vbNullString, and the BSTR it pointed at is where it was: pointing at it again is the String again.
    [TestMethod]
    public void APointerOfZero_IsvbNullString_AndTheBSTRIsWhereItWas()
    {
        var (storage, cell, address) = StringVariable(new VBRuntimeValue<string>("abc"));
        var characters = PointerOf(storage, address);

        PokePointer(storage, address, 0);
        Assert.IsNull(cell.Value.BoxedValue);
        Assert.AreEqual(0, PointerOf(storage, address));

        PokePointer(storage, address, characters);
        Assert.AreEqual("abc", cell.Value.BoxedValue);
    }

    // a pointer is written a byte at a time: until it points at a BSTR, the String is what it was, and its bytes are what was written.
    [TestMethod]
    public void APointerHalfWritten_LeavesTheString_AndReadsAsWritten()
    {
        var (storage, cell, address) = StringVariable(new VBRuntimeValue<string>("abc"));
        var characters = PointerOf(storage, address);

        Assert.IsTrue(storage.TryPoke(address, 0xFF));

        Assert.AreEqual("abc", cell.Value.BoxedValue);
        Assert.AreEqual(0xFF, Peek(storage, address.Value, 1)[0]);
        Assert.IsTrue(storage.TryPoke(address, (byte)(characters & 0xFF)));
        Assert.AreEqual(characters, BitConverter.ToInt64(Peek(storage, address.Value, 8)));
    }

    // grid-p3 Q05: an element given another's characters.
    [TestMethod]
    public void AnElementGivenAnothersCharacters_HoldsTheSameString()
    {
        var (storage, s, sAddress) = StringVariable(new VBRuntimeValue<string>("hey"));
        var array = new VBFixedSizeArrayValue([(0, 1)], VBStringType.TypeInfo);
        Assert.IsTrue(storage.TryAddressOfElement(array, 1, out var element));

        PokePointer(storage, element, PointerOf(storage, sAddress));

        Assert.AreEqual("hey", ((VBStringValue)array.ElementAt(1)!).Value);
        Assert.AreEqual(PointerOf(storage, sAddress), PointerOf(storage, element));
        PokePointer(storage, element, 0);
        Assert.AreEqual(0, PointerOf(storage, element));
        Assert.AreEqual("hey", s.Value.BoxedValue);
    }

    // grid-p3 Q04: a field given another's characters.
    [TestMethod]
    public void AFieldGivenAnothersCharacters_HoldsTheSameString()
    {
        var (storage, _, sAddress) = StringVariable(new VBRuntimeValue<string>("hey"));
        var record = new VBUserDefinedTypeValue(Named());
        var handle = new ValueBindingHandle(new VBRuntimeValue<VBRuntimeUserDefinedTypeValue>(new VBRuntimeUserDefinedTypeValue(record)));
        Assert.IsTrue(storage.TryAllocate(record.Size, handle, out var address));
        var field = new MemoryAddress(address.Value + 8);

        PokePointer(storage, field, PointerOf(storage, sAddress));

        Assert.AreEqual("hey", ((VBStringValue)record.FieldAt(1)!).Value);
        Assert.AreEqual(PointerOf(storage, sAddress), PointerOf(storage, field));
    }

    [TestMethod]
    public void FreeingTheVariable_FreesItsCharacters()
    {
        var (storage, _, address) = StringVariable(new VBRuntimeValue<string>("abc"));
        var characters = PointerOf(storage, address);

        Assert.IsTrue(storage.TryDeallocate(address));

        Assert.IsFalse(storage.TryPeek(new MemoryAddress((int)characters), out _));
    }

    // S08: a Variant holding a String has characters of its own.
    [TestMethod]
    public void AVariantHoldingAString_HasCharacters()
    {
        var storage = Storage();
        var variant = new ValueBindingHandle(new VBRuntimeVariantValue(VBVarType.VBString, new VBStringValue("hey")));
        Assert.IsTrue(storage.TryAllocate(24, variant, out var address));

        var characters = PointerOf(storage, address);

        Assert.AreNotEqual(0, characters);
        CollectionAssert.AreEqual(new byte[] { 104, 0 }, Peek(storage, characters, 2));
    }

    [TestMethod]
    public void WhatHoldsNoString_HasNoCharacters()
    {
        var storage = Storage();
        Assert.IsTrue(storage.TryAllocate(4, new ValueBindingHandle(new VBRuntimeValue<int>(5)), out var address));

        Assert.IsFalse(storage.TryGetStringPointer(address, out _));
    }

    // S10: an element of a String array is the address of its characters, 0 for one nothing was assigned to.
    [TestMethod]
    public void AStringElementsBytes_AreTheAddressOfItsCharacters()
    {
        var storage = Storage();
        var array = new VBFixedSizeArrayValue([(0, 1)], VBStringType.TypeInfo);
        array.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<string>("x")), 0);
        Assert.IsTrue(storage.TryAddressOfElement(array, 0, out var first));
        Assert.IsTrue(storage.TryAddressOfElement(array, 1, out var second));

        var characters = PointerOf(storage, first);

        Assert.AreEqual(characters, BitConverter.ToInt64(Peek(storage, first.Value, 8)));
        CollectionAssert.AreEqual(new byte[] { 120, 0, 0, 0 }, Peek(storage, characters, 4));
        Assert.AreEqual(0, BitConverter.ToInt64(Peek(storage, second.Value, 8)));
    }

    [TestMethod]
    public void APokeIntoAnElementsCharacters_ChangesTheElement()
    {
        var storage = Storage();
        var array = new VBFixedSizeArrayValue([(0, 0)], VBStringType.TypeInfo);
        array.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<string>("x")), 0);
        Assert.IsTrue(storage.TryAddressOfElement(array, 0, out var element));

        Assert.IsTrue(storage.TryPoke(new MemoryAddress((int)PointerOf(storage, element)), 121));

        Assert.AreEqual("y", ((VBStringValue)array.ElementAt(0)!).Value);
    }

    private static (SessionStorage Storage, VBUserDefinedTypeValue Record, VBFixedSizeArrayValue Field, MemoryAddress Address) WithArrayField(VBType itemType)
    {
        var uri = TestUri.TestModuleUserDefinedTypeUri("TInline");
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, "TInline", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var record = new VBUserDefinedTypeValue(new VBUserDefinedType(symbol, [Field("B", VBByteType.TypeInfo), Field("N", new VBFixedSizeArrayType(itemType))]));
        var field = new VBFixedSizeArrayValue([(0, 2)], itemType);
        Assert.IsTrue(record.TrySetFieldAt(1, field));
        field = (VBFixedSizeArrayValue)record.FieldAt(1)!;

        var storage = Storage();
        var handle = new ValueBindingHandle(new VBRuntimeValue<VBRuntimeUserDefinedTypeValue>(new VBRuntimeUserDefinedTypeValue(record)));
        Assert.IsTrue(storage.TryAllocate(record.Size, handle, out var address));
        return (storage, record, field, address);
    }

    // oracle\s24 P04: a fixed-size array field is in the record, its elements side by side - and its bytes are theirs.
    [TestMethod]
    public void AFixedSizeArrayFieldsElements_AreTheirBytes_InTheRecord()
    {
        var (storage, _, field, address) = WithArrayField(VBLongType.TypeInfo);
        Assert.IsTrue(field.TrySetElementAt(1, new ValueBindingHandle(new VBRuntimeValue<int>(300))));

        Assert.AreEqual(300, BitConverter.ToInt32(Peek(storage, address.Value + 8, 4)), "N(1) is 4 bytes past N(0), at offset 4");
        Assert.IsTrue(storage.TryPoke(new MemoryAddress(address.Value + 12), 7));

        Assert.AreEqual(7, ((VBLongValue)field.ElementAt(2)!).Value);
    }

    [TestMethod]
    public void AStringElementOfAFixedSizeArrayField_IsTheAddressOfItsCharacters()
    {
        var (storage, _, field, address) = WithArrayField(VBStringType.TypeInfo);
        Assert.IsTrue(field.TrySetElementAt(1, new ValueBindingHandle(new VBRuntimeValue<string>("q"))));
        var element = new MemoryAddress(address.Value + 16);

        var characters = PointerOf(storage, element);

        Assert.AreEqual(characters, BitConverter.ToInt64(Peek(storage, element.Value, 8)));
        Assert.AreEqual(0, BitConverter.ToInt64(Peek(storage, address.Value + 8, 8)), "N(0) holds vbNullString");
        Assert.IsTrue(storage.TryPoke(new MemoryAddress((int)characters), 114));
        Assert.AreEqual("r", ((VBStringValue)field.ElementAt(1)!).Value);
    }

    // a record's first field is at the record's address, which is the field's and not a String variable's.
    [TestMethod]
    public void AStringFieldAtTheStartOfARecord_IsTheFieldsString()
    {
        var uri = TestUri.TestModuleUserDefinedTypeUri("TFirst");
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, "TFirst", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var record = new VBUserDefinedTypeValue(new VBUserDefinedType(symbol, [Field("S", VBStringType.TypeInfo), Field("B", VBByteType.TypeInfo)]));
        record.TrySetFieldAt(0, new VBStringValue("hey"));
        var storage = Storage();
        var handle = new ValueBindingHandle(new VBRuntimeValue<VBRuntimeUserDefinedTypeValue>(new VBRuntimeUserDefinedTypeValue(record)));
        Assert.IsTrue(storage.TryAllocate(record.Size, handle, out var address));

        var characters = PointerOf(storage, address);

        CollectionAssert.AreEqual(new byte[] { 104, 0 }, Peek(storage, characters, 2));
        Assert.AreEqual(characters, BitConverter.ToInt64(Peek(storage, address.Value, 8)));
        Assert.IsTrue(storage.TryPoke(new MemoryAddress(address.Value + 8), 7), "the record's Byte field, not a String's pointer");
        Assert.AreEqual(7, ((VBByteValue)record.FieldAt(1)!).Value);
    }

    // S11: a String field of a record is the address of its characters, at the field's offset.
    [TestMethod]
    public void AStringFieldsBytes_AreTheAddressOfItsCharacters()
    {
        var storage = Storage();
        var record = new VBUserDefinedTypeValue(Named());
        record.TrySetFieldAt(1, new VBStringValue("hey"));
        var handle = new ValueBindingHandle(new VBRuntimeValue<VBRuntimeUserDefinedTypeValue>(new VBRuntimeUserDefinedTypeValue(record)));
        Assert.IsTrue(storage.TryAllocate(record.Size, handle, out var address));
        var field = new MemoryAddress(address.Value + 8);

        var characters = PointerOf(storage, field);

        Assert.AreEqual(characters, BitConverter.ToInt64(Peek(storage, field.Value, 8)));
        CollectionAssert.AreEqual(new byte[] { 104, 0 }, Peek(storage, characters, 2));

        Assert.IsTrue(storage.TryPoke(new MemoryAddress((int)characters), 115));
        Assert.AreEqual("sey", ((VBStringValue)record.FieldAt(1)!).Value);
    }
}
