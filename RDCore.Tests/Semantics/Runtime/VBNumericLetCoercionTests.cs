using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.1 Let-coercion between numeric types")]
public sealed class VBNumericLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static VBNumericLetCoercionTypeRuntimeSemantics Sut() => new(Formatter(), FakeProvider());

    [TestMethod]
    // floating -> integral : banker's rounding (MS-VBAL 5.5.1.2.1.1)
    [DataRow(2.5, (short)2)]
    [DataRow(3.5, (short)4)]
    [DataRow(2.67, (short)3)]
    [DataRow(-2.67, (short)-3)]
    public void FloatToInteger_RoundsBankers(double source, short expected)
        => AssertCoercedTo<VBIntegerValue>(Coerce(Sut(), new VBDoubleValue(source), VBIntegerType.TypeInfo), expected);

    [TestMethod]
    public void IntegerToLong_WidensAsCopy()
        => AssertCoercedTo<VBLongValue>(Coerce(Sut(), new VBIntegerValue((short)300), VBLongType.TypeInfo), 300);

    [TestMethod]
    public void IntegerToDouble_WidensAsCopy()
        => AssertCoercedTo<VBDoubleValue>(Coerce(Sut(), new VBIntegerValue((short)7), VBDoubleType.TypeInfo), 7d);

    [TestMethod]
    public void LongToInteger_InRange_NarrowsAsCopy()
        => AssertCoercedTo<VBIntegerValue>(Coerce(Sut(), new VBLongValue(1234), VBIntegerType.TypeInfo), (short)1234);

    [TestMethod]
    public void LongToInteger_OutOfRange_Overflows()
        => AssertError(Coerce(Sut(), new VBLongValue(100_000), VBIntegerType.TypeInfo), VBRuntimeErrorId.Overflow);
}
