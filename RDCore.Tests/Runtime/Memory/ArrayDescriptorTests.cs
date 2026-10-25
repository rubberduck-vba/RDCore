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
/// The <c>SAFEARRAY</c> a dynamic array, and a <c>Variant</c> holding an array, is a pointer to in the session's memory (<see cref="ArrayDescriptor"/>), as
/// MS-VBA lays one out (recorded in Excel, oracle\s17 grid-p4 A01-A12, grid-p4b B01-B12).
/// </summary>
[TestClass]
public sealed class ArrayDescriptorTests
{
    private static SessionStorage Storage(Func<VBArrayValue, int>? locks = null)
        => new(new SessionMemory(new(), PointerSize.x64, SessionMemory.FirstAddress), pointerSize: 8) { LocksOf = locks };

    private static VBUserDefinedTypeFieldSymbol Field(string name, VBType type)
    {
        var uri = TestUri.TestUserDefinedTypeMemberUri(name);
        return new VBUserDefinedTypeFieldSymbol(uri, uri, name, type, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
    }

    // a record whose first field is a dynamic array: its address is the field's (`Private Type TDyn: A() As Long: End Type`).
    private static (SessionStorage Storage, VBUserDefinedTypeValue Record, MemoryAddress Address) WithDynamicField(VBType itemType, Func<VBArrayValue, int>? locks = null)
    {
        var uri = TestUri.TestModuleUserDefinedTypeUri("TDyn");
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, "TDyn", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var record = new VBUserDefinedTypeValue(new VBUserDefinedType(symbol, [Field("A", new VBResizableArrayType(itemType)), Field("B", VBByteType.TypeInfo)]));
        var storage = Storage(locks);
        var handle = new ValueBindingHandle(new VBRuntimeValue<VBRuntimeUserDefinedTypeValue>(new VBRuntimeUserDefinedTypeValue(record)));
        Assert.IsTrue(storage.TryAllocate(record.Size, handle, out var address));
        return (storage, record, address);
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

    private static long PeekPointer(SessionStorage storage, long at) => BitConverter.ToInt64(Peek(storage, at, 8));

    private static int[] Header(SessionStorage storage, long descriptor)
    {
        var bytes = Peek(storage, descriptor, 12);
        return [BitConverter.ToInt16(bytes, 0), BitConverter.ToInt16(bytes, 2), BitConverter.ToInt32(bytes, 4), BitConverter.ToInt32(bytes, 8)];
    }

    // A01: an array with no dimensions is a pointer that is 0.
    [TestMethod]
    public void ADynamicArrayWithNoDimensions_IsZero()
    {
        var (storage, record, address) = WithDynamicField(VBLongType.TypeInfo);
        Assert.IsTrue(record.TrySetFieldAt(0, new VBResizableArrayValue([], VBLongType.TypeInfo)));

        Assert.AreEqual(0, PeekPointer(storage, address.Value));
    }

    // A02: the dimensions, the features, the width of an element, the locks, the address of the elements, then each dimension's count and lower bound, the
    // last dimension first.
    [TestMethod]
    public void ADescriptor_IsLaidOutAsMSVBALaysItOut()
    {
        var (storage, record, address) = WithDynamicField(VBLongType.TypeInfo);
        var array = new VBResizableArrayValue([(1, 3), (0, 4)], VBLongType.TypeInfo);
        Assert.IsTrue(record.TrySetFieldAt(0, array));

        var descriptor = PeekPointer(storage, address.Value);
        var held = (VBArrayValue)record.FieldAt(0)!;
        Assert.IsTrue(storage.TryAddressOfElement(held, 0, out var first));

        CollectionAssert.AreEqual(new[] { 2, 128, 4, 0 }, Header(storage, descriptor));
        Assert.AreEqual(first.Value, PeekPointer(storage, descriptor + 16));
        var bounds = Peek(storage, descriptor + 24, 16);
        CollectionAssert.AreEqual(new[] { 5, 0, 3, 1 }, new[] { BitConverter.ToInt32(bounds, 0), BitConverter.ToInt32(bounds, 4), BitConverter.ToInt32(bounds, 8), BitConverter.ToInt32(bounds, 12) });
    }

    // A03, A08-A10, B01-B06, B09: the features and the width of an element say what the elements are.
    [TestMethod]
    [DataRow("String", 384, 8)]
    [DataRow("Variant", 2176, 24)]
    [DataRow("Object", 1088, 8)]
    [DataRow("Date", 128, 8)]
    [DataRow("Boolean", 128, 2)]
    [DataRow("Byte", 128, 1)]
    [DataRow("Currency", 128, 8)]
    [DataRow("LongLong", 128, 8)]
    public void TheFeaturesAndTheWidth_SayWhatTheElementsAre(string typeName, int features, int width)
    {
        Assert.IsTrue(IntrinsicVBTypes.TryResolve(typeName, out var type));
        var (storage, record, address) = WithDynamicField(type!);
        Assert.IsTrue(record.TrySetFieldAt(0, new VBResizableArrayValue([(0, 1)], type)));

        var header = Header(storage, PeekPointer(storage, address.Value));

        Assert.AreEqual(features, header[1]);
        Assert.AreEqual(width, header[2]);
    }

    // A03: the elements of a String array are the addresses of their characters.
    [TestMethod]
    public void TheElementsOfAStringArray_AreTheAddressesOfTheirCharacters()
    {
        var (storage, record, address) = WithDynamicField(VBStringType.TypeInfo);
        Assert.IsTrue(record.TrySetFieldAt(0, new VBResizableArrayValue([(0, 1)], VBStringType.TypeInfo)));
        var held = (VBArrayValue)record.FieldAt(0)!;
        held.TrySetElementAt(1, new ValueBindingHandle(new VBRuntimeValue<string>("x")));

        var elements = PeekPointer(storage, PeekPointer(storage, address.Value) + 16);
        Assert.IsTrue(storage.TryGetStringPointer(new MemoryAddress((int)elements + 8), out var characters));

        Assert.AreEqual(characters, PeekPointer(storage, elements + 8));
    }

    // A06: a dynamic array's descriptor is the same for as long as it holds an array, ReDim'd or not; its elements are elsewhere; an Erase'd one is 0.
    [TestMethod]
    public void ADynamicArraysDescriptor_IsTheSameAcrossAReDim()
    {
        var (storage, record, address) = WithDynamicField(VBLongType.TypeInfo);
        Assert.IsTrue(record.TrySetFieldAt(0, new VBResizableArrayValue([(0, 2)], VBLongType.TypeInfo)));
        var descriptor = PeekPointer(storage, address.Value);
        var elements = PeekPointer(storage, descriptor + 16);

        Assert.IsTrue(record.TrySetFieldAt(0, new VBResizableArrayValue([(0, 5)], VBLongType.TypeInfo)));
        Assert.AreEqual(descriptor, PeekPointer(storage, address.Value));
        Assert.AreNotEqual(elements, PeekPointer(storage, descriptor + 16));
        Assert.AreEqual(6, BitConverter.ToInt32(Peek(storage, descriptor + 24, 4)));

        Assert.IsTrue(record.TrySetFieldAt(0, new VBResizableArrayValue([], VBLongType.TypeInfo)));
        Assert.AreEqual(0, PeekPointer(storage, address.Value));
        Assert.IsFalse(storage.TryPeek(new MemoryAddress((int)descriptor), out _), "the descriptor is freed");
    }

    // A07: the locks are how many times the array is locked.
    [TestMethod]
    public void TheLocks_AreHowManyTimesTheArrayIsLocked()
    {
        var locked = 0;
        var (storage, record, address) = WithDynamicField(VBLongType.TypeInfo, _ => locked);
        Assert.IsTrue(record.TrySetFieldAt(0, new VBResizableArrayValue([(0, 2)], VBLongType.TypeInfo)));
        var descriptor = PeekPointer(storage, address.Value);

        locked = 1;

        Assert.AreEqual(1, Header(storage, descriptor)[3]);
    }

    // A04, B11: a Variant holding an array is a pointer to its descriptor, which is the array's: another array assigned to it has another.
    [TestMethod]
    public void AVariantHoldingAnArray_IsAPointerToItsDescriptor()
    {
        var storage = Storage();
        var array = new VBResizableArrayValue([(0, 1)], VBVariantType.TypeInfo);
        var cell = new ValueBindingHandle(new VBRuntimeVariantValue(VBVarType.VBArray | VBVarType.VBVariant, array));
        Assert.IsTrue(storage.TryAllocate(0, cell, out var address));

        Assert.AreEqual(8204, BitConverter.ToInt16(Peek(storage, address.Value, 2)));
        var descriptor = PeekPointer(storage, address.Value + 8);
        CollectionAssert.AreEqual(new[] { 1, 2176, 24, 0 }, Header(storage, descriptor));
        Assert.AreEqual(2, BitConverter.ToInt32(Peek(storage, descriptor + 24, 4)));

        cell.Store(new VBRuntimeVariantValue(VBVarType.VBArray | VBVarType.VBVariant, new VBResizableArrayValue([(0, 0)], VBVariantType.TypeInfo)), isKnown: true);
        Assert.AreNotEqual(descriptor, PeekPointer(storage, address.Value + 8));
    }

    // A12: a lower bound poked moves the elements to other subscripts, where they are.
    [TestMethod]
    public void ALowerBoundPoked_MovesTheElementsToOtherSubscripts()
    {
        var (storage, record, address) = WithDynamicField(VBLongType.TypeInfo);
        var array = new VBResizableArrayValue([(0, 2)], VBLongType.TypeInfo);
        array.TrySetElementAt(1, new ValueBindingHandle(new VBRuntimeValue<int>(5)));
        Assert.IsTrue(record.TrySetFieldAt(0, array));
        var descriptor = PeekPointer(storage, address.Value);
        var elements = PeekPointer(storage, descriptor + 16);

        Assert.IsTrue(storage.TryPoke(new MemoryAddress((int)descriptor + 28), 1));

        var reshaped = (VBArrayValue)record.FieldAt(0)!;
        Assert.AreEqual(1, reshaped.Dimensions[0].LowerBound);
        Assert.AreEqual(5, ((VBLongValue)reshaped[[2]]!).Value, "a(2) is what a(1) was");
        Assert.AreEqual(elements, PeekPointer(storage, descriptor + 16), "the elements are where they were");
    }

    // A11: a count poked smaller makes the array shorter; one that makes it longer than it is is memory it is not, and is refused.
    [TestMethod]
    public void ACountPoked_MakesTheArrayShorter_NotLonger()
    {
        var (storage, record, address) = WithDynamicField(VBLongType.TypeInfo);
        Assert.IsTrue(record.TrySetFieldAt(0, new VBResizableArrayValue([(0, 2)], VBLongType.TypeInfo)));
        var descriptor = PeekPointer(storage, address.Value);

        Assert.IsFalse(storage.TryPoke(new MemoryAddress((int)descriptor + 24), 4));
        Assert.IsTrue(storage.TryPoke(new MemoryAddress((int)descriptor + 24), 2));

        Assert.AreEqual(1, ((VBArrayValue)record.FieldAt(0)!).Dimensions[0].UpperBound);
    }

    [TestMethod]
    public void TheDimensionsOrTheFeatures_AreNotWrittenByAPoke()
    {
        var (storage, record, address) = WithDynamicField(VBLongType.TypeInfo);
        Assert.IsTrue(record.TrySetFieldAt(0, new VBResizableArrayValue([(0, 2)], VBLongType.TypeInfo)));
        var descriptor = PeekPointer(storage, address.Value);

        Assert.IsFalse(storage.TryPoke(new MemoryAddress((int)descriptor), 2));
        Assert.IsFalse(storage.TryPoke(new MemoryAddress((int)descriptor + 16), 0));
    }

    // B12: another's descriptor poked into a dynamic array makes it the same array; 0 makes it one with no dimensions.
    [TestMethod]
    public void AnothersDescriptorPoked_MakesItTheSameArray()
    {
        var (storage, record, address) = WithDynamicField(VBLongType.TypeInfo);
        var array = new VBResizableArrayValue([(0, 2)], VBLongType.TypeInfo);
        Assert.IsTrue(record.TrySetFieldAt(0, array));
        var held = (VBArrayValue)record.FieldAt(0)!;
        var other = new VBUserDefinedTypeValue((VBUserDefinedType)record.TypeInfo);
        var handle = new ValueBindingHandle(new VBRuntimeValue<VBRuntimeUserDefinedTypeValue>(new VBRuntimeUserDefinedTypeValue(other)));
        Assert.IsTrue(storage.TryAllocate(other.Size, handle, out var otherAddress));
        var descriptor = PeekPointer(storage, address.Value);

        var bytes = BitConverter.GetBytes(descriptor);
        for (var index = 0; index < bytes.Length; index++)
        {
            Assert.IsTrue(storage.TryPoke(new MemoryAddress(otherAddress.Value + index), bytes[index]));
        }

        Assert.AreSame(held, other.FieldAt(0));
        foreach (var index in Enumerable.Range(0, 8))
        {
            Assert.IsTrue(storage.TryPoke(new MemoryAddress(otherAddress.Value + index), 0));
        }

        Assert.IsFalse(((VBArrayValue)other.FieldAt(0)!).IsInitialized);
        Assert.AreSame(held, record.FieldAt(0));
    }
}
