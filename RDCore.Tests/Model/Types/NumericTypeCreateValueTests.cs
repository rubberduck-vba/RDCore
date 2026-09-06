using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Meta;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Tests.Model.Types;

[TestClass]
[TestCategory("RD-VBAL §2.4 Static Types")]
public sealed class NumericTypeCreateValueTests
{
    [TestMethod]
    public void CreateValue_Double_ConvertsToTheTargetRepresentation()
    {
        Assert.AreEqual((short)3, ((VBIntegerValue)VBIntegerType.TypeInfo.CreateValue(3.2)).Value);
        Assert.AreEqual((short)2, ((VBIntegerValue)VBIntegerType.TypeInfo.CreateValue(2.5)).Value);        // banker's: tie -> even
        Assert.AreEqual((short)4, ((VBIntegerValue)VBIntegerType.TypeInfo.CreateValue(3.5)).Value);        // banker's: tie -> even
        Assert.AreEqual(2_000_000_000, ((VBLongValue)VBLongType.TypeInfo.CreateValue(2_000_000_000d)).Value);
        Assert.AreEqual(1.5, ((VBDoubleValue)VBDoubleType.TypeInfo.CreateValue(1.5)).Value);
    }

    [TestMethod]
    public void CreateValue_BindingHandle_BindsWithoutConversion()
    {
        var handle = new ValueBindingHandle(new VBRuntimeValue<short>(9));
        Assert.AreSame(handle, VBIntegerType.TypeInfo.CreateValue(handle).Handle);
    }

    [TestMethod]
    public void CreateValue_BindingHandle_OnANonValueType_Throws()
        => Assert.ThrowsExactly<NotSupportedException>(
            () => VBTypeDesc.TypeInfo.CreateValue(InvalidBindingHandle.Default));
}
