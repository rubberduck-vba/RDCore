using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for Variant let-coercion (MS-VBAL §5.5.1.2.12): any source except a class
/// or Nothing wraps as a copy of itself; a class or Nothing source defers (Set-coercion territory).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.12 Let-coercion to Variant")]
public sealed class VBVariantTypeLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static VBVariantTypeLetCoercionRuntimeSemantics Sut() => new(FakeProvider(), Formatter());

    [TestMethod]
    public void NumericSource_WrapsAsAVariantCopy()
    {
        var result = Coerce(Sut(), new VBLongValue(5), VBVariantType.TypeInfo);
        Assert.IsTrue(result.IsApplicable);
        Assert.IsNull(result.ErrorInfo);
        Assert.IsInstanceOfType<VBVariantValue>(result.Result);
        Assert.AreEqual(5, ((VBVariantValue)result.Result!).TypedValue.RuntimeValue.BoxedValue);
    }

    [TestMethod]
    public void ObjectSource_Defers()
        // an object source is Set-coercion territory, not this strategy's concern.
        => Assert.IsFalse(Coerce(Sut(), new VBObjectValue(new MemoryAddress(1)), VBVariantType.TypeInfo).IsApplicable);
}
