using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Model.Values;

/// <summary>
/// <see cref="VBArrayValue"/> represents an N-dimensional array as bounds metadata plus one flat,
/// column-major cell store. These pin rank/bounds, element addressing and default initialization.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.5 Runtime Values")]
public sealed class VBArrayValueTests
{
    private static VBFixedSizeArrayValue Fixed((int lBound, int uBound)[] dims, VBType itemType)
        => new(dims, itemType);

    [TestMethod]
    public void OneDimensional_RankBoundsAndLength()
    {
        var a = Fixed([(1, 3)], VBLongType.TypeInfo);

        Assert.AreEqual(1, a.Rank);
        Assert.IsTrue(a.IsInitialized);
        var (lower, upper) = a.Dimensions[0];
        Assert.AreEqual((1, 3), (lower, upper));
        Assert.AreEqual(3, a.Length);
    }

    [TestMethod]
    public void NonZeroLowerBound_LengthIsInclusive()
        // regression: VBArrayDimension used Enumerable.Range(lBound, uBound) (start, count).
        => Assert.AreEqual(4, Fixed([(2, 5)], VBLongType.TypeInfo).Length);

    [TestMethod]
    public void TwoDimensional_TotalElementCount()
    {
        var a = Fixed([(1, 3), (1, 4)], VBLongType.TypeInfo);
        Assert.AreEqual(2, a.Rank);
        Assert.AreEqual(12, a.Length);
    }

    [TestMethod]
    public void ColumnMajor_FirstSubscriptVariesFastest_NoCrossTalk()
    {
        var a = Fixed([(1, 2), (1, 3)], VBLongType.TypeInfo); // 6 cells

        for (var j = 1; j <= 3; j++)
        {
            for (var i = 1; i <= 2; i++)
            {
                Assert.IsTrue(a.TrySetElement(Handle(i * 10 + j), i, j));
            }
        }

        for (var j = 1; j <= 3; j++)
        {
            for (var i = 1; i <= 2; i++)
            {
                Assert.AreEqual(i * 10 + j, ((VBLongValue)a[i, j]!).Value, $"a({i},{j})");
            }
        }
    }

    [TestMethod]
    public void Subscript_OutOfRange_ReturnsNull()
    {
        var a = Fixed([(1, 3)], VBLongType.TypeInfo);
        Assert.IsNull(a[0]);
        Assert.IsNull(a[4]);
    }

    [TestMethod]
    public void Subscript_WrongRank_ReturnsNull()
        => Assert.IsNull(Fixed([(1, 3)], VBLongType.TypeInfo)[1, 2]);

    [TestMethod]
    public void Uninitialized_HasNoDimensionsOrCells()
    {
        var a = new VBResizableArrayValue([]);
        Assert.AreEqual(0, a.Rank);
        Assert.AreEqual(0, a.Length);
        Assert.IsFalse(a.IsInitialized);
        Assert.IsNull(a[0]);
    }

    [TestMethod]
    public void LongCells_DefaultToZero()
        => Assert.AreEqual(0, ((VBLongValue)Fixed([(1, 2)], VBLongType.TypeInfo)[1]!).Value);

    [TestMethod]
    public void BooleanCells_DefaultToFalse()
        => Assert.IsFalse((bool)((VBBooleanValue)Fixed([(1, 2)], VBBooleanType.TypeInfo)[1]!).Value);

    [TestMethod]
    public void ByteArrayCells_DefaultToZero()
        => Assert.AreEqual((byte)0, ((VBByteValue)new VBResizableByteArrayValue([(0, 2)])[0]!).Value);

    [TestMethod]
    public void StringCells_AreStringValues()
        // default is the null string; VBStringValue.Value on a null-backed value is a separate concern.
        => Assert.IsInstanceOfType<VBStringValue>(Fixed([(1, 2)], VBStringType.TypeInfo)[1]);

    [TestMethod]
    public void SetElement_MutatesTheCell()
    {
        var a = Fixed([(1, 3)], VBLongType.TypeInfo);
        Assert.IsTrue(a.TrySetElement(Handle(42), 2));
        Assert.AreEqual(42, ((VBLongValue)a[2]!).Value);
        Assert.AreEqual(0, ((VBLongValue)a[1]!).Value); // neighbour untouched
    }

    [TestMethod]
    public void SetElement_OutOfRange_ReturnsFalse()
        => Assert.IsFalse(Fixed([(1, 3)], VBLongType.TypeInfo).TrySetElement(Handle(1), 9));

    [TestMethod]
    public void DerivedTypeIdentity_IsPreserved()
    {
        Assert.IsInstanceOfType<VBArrayValue>(Fixed([(1, 2)], VBLongType.TypeInfo));
        Assert.IsInstanceOfType<VBFixedSizeArrayValue>(Fixed([(1, 2)], VBLongType.TypeInfo));
        Assert.IsInstanceOfType<VBResizableArrayValue>(new VBResizableByteArrayValue([(0, 1)]));
    }

    [TestMethod]
    public void Size_IsElementSizeTimesCount()
        => Assert.AreEqual(sizeof(short) * 4, Fixed([(1, 4)], VBIntegerType.TypeInfo).Size);

    [TestMethod]
    public void BindingHandleCtor_KeepsThePassedHandle()
    {
        var handle = InvalidBindingHandle.Default;
        Assert.AreSame(handle, new VBFixedSizeArrayValue(handle, [(0, 2)], VBIntegerType.TypeInfo).Handle);
    }

    private static ValueBindingHandle Handle(int value) => new(new VBRuntimeValue<int>(value));

    [TestMethod]
    public void TryAllocateIn_BindsTheAllocatedAddressAsItsOwnValue_AndPreservesDerivedType()
    {
        var array = Fixed([(1, 3)], VBLongType.TypeInfo);
        var storage = new SessionStorage(new SessionMemory(new(), PointerSize.x86));

        Assert.IsTrue(array.TryAllocateIn(storage, out var allocated));

        Assert.IsInstanceOfType<VBFixedSizeArrayValue>(allocated);
        var address = ((VBRuntimeReference)allocated.RuntimeValue).Value;
        Assert.IsTrue(storage.TryRead(address, out var bound));
        Assert.AreSame(allocated.Handle, bound);
    }

    [TestMethod]
    // renamed from TryAllocateIn_KeepsTheSameCells: this asserts CONTENT is preserved at allocation
    // time, which stays true whether the cells array is shared or deep-copied. See
    // TryAllocateIn_GivesTheAllocatedCopyIndependentCells for the aliasing regression this session's
    // adversarial review found - "keeps the same cells" used to (accidentally, coincidentally) also
    // describe the bug: the two values literally shared one array object.
    public void TryAllocateIn_PreservesElementValuesSetBeforeAllocation()
    {
        var array = Fixed([(1, 3)], VBLongType.TypeInfo);
        array.TrySetElement(Handle(42), 2);
        var storage = new SessionStorage(new SessionMemory(new(), PointerSize.x86));

        array.TryAllocateIn(storage, out var allocated);

        Assert.AreEqual(42, ((VBLongValue)allocated![2]!).Value);
    }

    [TestMethod]
    // adversarial review, PRs #208-224, item 5: TryAllocateIn's `with`-copy shared the SAME _cells
    // array by reference with the original - VBA deep-copies an array on assignment (MS-VBAL), so two
    // allocations of "the same" array value aliased in a way VBA arrays do not have. Author's call:
    // "we must stick to the expected behavior" - a write through either value's own element accessor
    // must not be visible through the other's.
    public void TryAllocateIn_GivesTheAllocatedCopyIndependentCells()
    {
        var array = Fixed([(1, 3)], VBLongType.TypeInfo);
        var storage = new SessionStorage(new SessionMemory(new(), PointerSize.x86));
        array.TryAllocateIn(storage, out var allocated);

        array.TrySetElement(Handle(1), 2);
        allocated!.TrySetElement(Handle(2), 2);

        Assert.AreEqual(1, ((VBLongValue)array[2]!).Value);
        Assert.AreEqual(2, ((VBLongValue)allocated[2]!).Value);
    }

    [TestMethod]
    public void TryAllocateIn_OutOfMemory_ReturnsFalse()
    {
        var array = Fixed([(1, 3)], VBLongType.TypeInfo);
        var storage = Substitute.For<ISessionStorage>();
        storage.TryAllocate(Arg.Any<int>(), Arg.Any<IBindingHandle>(), out Arg.Any<MemoryAddress>()).Returns(false);

        Assert.IsFalse(array.TryAllocateIn(storage, out var allocated));
        Assert.IsNull(allocated);
    }
}
