using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Tests.Model.Values;

/// <summary>
/// A <see cref="VBTypedValue"/>'s identity is its exact value type plus the managed value it holds —
/// never the (mutable) <see cref="IBindingHandle"/> nor a resolved symbol. These pin the
/// <see cref="object.Equals(object)"/> / <see cref="object.GetHashCode"/> contract for the model.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.5 Runtime Values")]
public sealed class VBTypedValueEqualityTests
{
    [TestMethod]
    public void SameTypeSameValue_AreEqual_AndHashEqual()
    {
        VBTypedValue a = new VBIntegerValue((short)5);
        VBTypedValue b = new VBIntegerValue((short)5);

        Assert.AreEqual(a, b);
        Assert.IsTrue(a == b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void SameTypeDifferentValue_AreNotEqual()
        => Assert.AreNotEqual<VBTypedValue>(new VBIntegerValue((short)5), new VBIntegerValue((short)6));

    [TestMethod]
    public void SameManagedValueDifferentType_AreNotEqual()
        => Assert.AreNotEqual<VBTypedValue>(new VBIntegerValue((short)5), new VBLongValue(5));

    [TestMethod]
    public void EqualityIgnoresTheBindingHandleKind()
    {
        // one value-bound, one constant-bound, same underlying runtime value.
        VBTypedValue valueBound = new VBLongValue(42);
        VBTypedValue constBound = new VBLongValue(new ConstantBindingHandle(new VBRuntimeValue<int>(42)));

        Assert.AreEqual(valueBound, constBound);
        Assert.AreEqual(valueBound.GetHashCode(), constBound.GetHashCode());
    }

    [TestMethod]
    public void GetHashCode_OnAnUnboundValue_DoesNotThrow()
    {
        // regression: GetHashCode read through Handle.Value, which throws for an invalid binding.
        VBTypedValue unbound = new VBIntegerValue(InvalidBindingHandle.Default);
        _ = unbound.GetHashCode();
        Assert.AreEqual(unbound, new VBIntegerValue(InvalidBindingHandle.Default));
    }

    [TestMethod]
    public void UsableAsDictionaryKey()
    {
        var map = new Dictionary<VBTypedValue, string>
        {
            [new VBLongValue(1)] = "one",
            [new VBStringValue("x")] = "ex",
        };

        Assert.AreEqual("one", map[new VBLongValue(1)]);
        Assert.AreEqual("ex", map[new VBStringValue("x")]);
        Assert.IsFalse(map.ContainsKey(new VBLongValue(2)));
    }

    [TestMethod]
    public void InterfaceEquals_AgreesWithRecordEquality_ForSameValue()
    {
        var a = new VBDoubleValue(3.5);
        var b = new VBDoubleValue(3.5);

        Assert.IsTrue(a.Equals(b));                                  // record
        Assert.IsTrue(((IVBTypedValue<VBDoubleValue, double>)a).Equals(b)); // interface
    }

    [TestMethod]
    public void NullBackedStringValues_AreEqual()
        => Assert.AreEqual<VBTypedValue>(new VBStringValue((string)null!), new VBStringValue((string)null!));
}
