using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Tests.Model.Values;

/// <summary>
/// <see cref="VBVariantValue"/> wraps a whole <see cref="RDCore.SDK.Model.Values.Abstract.VBTypedValue"/>,
/// not a bare scalar - these pin that its own <c>Handle</c>/<c>RuntimeValue</c> round-trip the wrapped
/// value intact, the same guarantee <see cref="VBArrayValueTests"/> pins for an array's own cells.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.5 Runtime Values")]
public sealed class VBVariantValueTests
{
    [TestMethod]
    public void Construction_IsSelfConsistentlyBound_RuntimeValueDoesNotThrow()
        // a fresh VBVariantValue used to leave its own Handle at the base InvalidBindingHandle.Default,
        // so .RuntimeValue threw NotSupportedException the instant anything touched it - the exact crash
        // ParamArray element collection hit before this fix.
    {
        var variant = new VBVariantValue(new VBLongValue(5));

        var runtimeValue = variant.RuntimeValue;

        Assert.IsInstanceOfType<VBRuntimeVariantValue>(runtimeValue);
    }

    [TestMethod]
    public void CreateValue_UnboxesTheSameWrappedValue_NeverAFreshEmpty()
        // VBVariantType.CreateValue used to hardcode `new VBVariantValue(handle, VBEmptyValue.Empty)`,
        // discarding whatever was actually stored - any array-of-Variant or Variant-typed local/field
        // read back as Empty regardless of what it held.
    {
        var stored = new VBVariantValue(new VBLongValue(42));
        var handle = new ValueBindingHandle(stored.RuntimeValue);

        var recovered = (VBVariantValue)VBVariantType.TypeInfo.CreateValue(handle);

        Assert.AreEqual(42, recovered.TypedValue.RuntimeValue.BoxedValue);
    }

    [TestMethod]
    public void Construction_ComputesItsOwnVarTypeTagFromTheWrappedValue()
        // regression: the tag used to be hardcoded to Empty regardless of what was actually wrapped.
        => Assert.AreEqual(VBVarType.VBLong, new VBVariantValue(new VBLongValue(5)).Value.ValueType);

    [TestMethod]
    public void CreateValue_PreservesTheVarTypeTag_ThroughTheStorageRoundTrip()
    {
        var stored = new VBVariantValue(new VBStringValue("x"));
        var handle = new ValueBindingHandle(stored.RuntimeValue);

        var recovered = (VBVariantValue)VBVariantType.TypeInfo.CreateValue(handle);

        Assert.AreEqual(VBVarType.VBString, recovered.Value.ValueType);
    }

    [TestMethod]
    public void Construction_WrappingAnotherVariant_StillRoundTrips()
        // VBVariantValue's own ctor doc allows the wrapped value to itself be a Variant - the box/unbox
        // pair must not choke on that nesting.
    {
        var inner = new VBVariantValue(new VBLongValue(7));
        var outer = new VBVariantValue(inner);

        var handle = new ValueBindingHandle(outer.RuntimeValue);
        var recovered = (VBVariantValue)VBVariantType.TypeInfo.CreateValue(handle);

        Assert.IsInstanceOfType<VBVariantValue>(recovered.TypedValue);
    }
}
