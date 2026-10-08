using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Tests.Model.Values.Bindings;

/// <summary>
/// An indeterminate value is a value of a known type whose value is not known: it assumes its type's default value, so that the semantics that read it
/// can evaluate, and it is never mistaken for the value it assumes.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.5 Runtime Values")]
public sealed class IndeterminateBindingHandleTests
{
    public static IEnumerable<object[]> DeclaredTypes()
    {
        yield return [VBByteType.TypeInfo];
        yield return [VBBooleanType.TypeInfo];
        yield return [VBIntegerType.TypeInfo];
        yield return [VBLongType.TypeInfo];
        yield return [VBSingleType.TypeInfo];
        yield return [VBDoubleType.TypeInfo];
        yield return [VBCurrencyType.TypeInfo];
        yield return [VBDecimalType.TypeInfo];
        yield return [VBDateType.TypeInfo];
        yield return [VBStringType.TypeInfo];
        yield return [VBVariantType.TypeInfo];
        yield return [VBObjectType.TypeInfo];
    }

    [TestMethod]
    [DynamicData(nameof(DeclaredTypes))]
    public void CreateIndeterminateValue_IsIndeterminate_AndAssumesTheDefaultValue(VBType type)
    {
        var value = type.CreateIndeterminateValue();

        Assert.IsTrue(value.IsIndeterminate);
        Assert.AreEqual(type.DefaultValue.GetType(), value.GetType());
        Assert.AreEqual(type.DefaultValue.RuntimeValue.BoxedValue, value.RuntimeValue.BoxedValue);
    }

    [TestMethod]
    [DynamicData(nameof(DeclaredTypes))]
    public void AnIndeterminateValue_IsNotEqualToTheValueItAssumes(VBType type)
        => Assert.AreNotEqual(type.DefaultValue, type.CreateIndeterminateValue());

    [TestMethod]
    public void TwoIndeterminateValuesOfTheSameType_AreEqual()
    {
        var first = VBLongType.TypeInfo.CreateIndeterminateValue();
        var second = VBLongType.TypeInfo.CreateIndeterminateValue();

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    [TestMethod]
    public void AKnownValue_IsNotIndeterminate()
        => Assert.IsFalse(new VBLongValue(42).IsIndeterminate);

    [TestMethod]
    public void AsIndeterminate_AssumesTheValueItWasGiven()
    {
        var value = new VBLongValue(42).AsIndeterminate();

        Assert.IsTrue(value.IsIndeterminate);
        Assert.AreEqual(42, ((VBLongValue)value).Value);
    }

    [TestMethod]
    public void AsIndeterminate_OfAnIndeterminateValue_DoesNotNestTheBinding()
    {
        var value = new VBLongValue(42).AsIndeterminate();
        var again = value.AsIndeterminate();

        Assert.AreSame(value, again);
        Assert.IsNotInstanceOfType<IndeterminateBindingHandle>(((IndeterminateBindingHandle)again.Handle).Assumed);
    }

    [TestMethod]
    public void AnIndeterminateValue_IsNeverAssigned()
    {
        var handle = VBLongType.TypeInfo.CreateIndeterminateValue().Handle;

        Assert.IsFalse(handle.BindingCapabilities.HasFlag(BindingCapabilities.SetValue));
        Assert.IsTrue(handle.BindingCapabilities.HasFlag(BindingCapabilities.GetValue));
        Assert.ThrowsExactly<NotSupportedException>(() => handle.SetValue(null!, new VBRuntimeValue<int>(1)));
    }

    [TestMethod]
    public void AnIndeterminateValue_PrintsAsIndeterminate()
        => Assert.Contains("Indeterminate", VBLongType.TypeInfo.CreateIndeterminateValue().ToString());

    [TestMethod]
    public void AnIndeterminateVariant_WrapsItsDefaultSubtype()
    {
        var value = (VBVariantValue)VBVariantType.TypeInfo.CreateIndeterminateValue();

        Assert.IsTrue(value.IsIndeterminate);
        Assert.IsInstanceOfType<VBEmptyValue>(value.TypedValue);
    }
}
