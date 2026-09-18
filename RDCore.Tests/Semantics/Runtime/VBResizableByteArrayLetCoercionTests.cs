using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for ResizableByteArray let-coercion (MS-VBAL §5.5.1.2.6).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.6 Let-coercion to and from a resizable Byte array")]
public sealed class VBResizableByteArrayLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static VBResizableByteArrayLetCoercionRuntimeSemantics Sut() => new(FakeProvider(), Formatter());

    [TestMethod]
    public void StringSource_CoercesToByteArray()
        // "x" is U+0078, so its UTF-16LE storage is the two bytes 0x78, 0x00.
        => CollectionAssert.AreEqual(new byte[] { (byte)'x', 0 },
            BytesOf(Coerce(Sut(), new VBStringValue("x"), VBResizableByteArrayType.TypeInfo)));

    [TestMethod]
    public void EmptyStringSource_CoercesToZeroLengthByteArray()
        => Assert.AreEqual(0, BytesOf(Coerce(Sut(), VBStringValue.ZeroLengthString, VBResizableByteArrayType.TypeInfo)).Length);

    [TestMethod]
    public void ByteArraySource_CopiesElementsAndPreservesBounds()
    {
        var source = new VBResizableByteArrayValue([(5, 6)]);
        source.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<byte>(1)), 5);
        source.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<byte>(2)), 6);

        var result = Coerce(Sut(), source, VBResizableByteArrayType.TypeInfo);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        var copy = (VBResizableByteArrayValue)result.Result!;
        Assert.AreEqual((5, 6), (copy.Dimensions[0].LowerBound, copy.Dimensions[0].UpperBound));
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, BytesOf(result));
    }

    [TestMethod]
    public void NumericSource_IsTypeMismatch()
        => AssertError(Coerce(Sut(), new VBLongValue(5), VBResizableByteArrayType.TypeInfo), VBRuntimeErrorId.TypeMismatch);

    private static byte[] BytesOf(LetCoercionResult result)
    {
        var array = (VBResizableByteArrayValue)result.Result!;
        if (!array.IsInitialized)
        {
            return [];
        }
        var (lower, upper) = array.Dimensions[0];
        return [.. Enumerable.Range(lower, upper - lower + 1).Select(i => ((VBByteValue)array[i]!).Value)];
    }
}
