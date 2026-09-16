using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for ResizableByteArray let-coercion (MS-VBAL §5.5.1.2.6). Neither half of
/// the strategy is implemented yet (both <c>EvaluateLetCoercion</c> and its semantic-analysis
/// counterpart unconditionally throw) — tracked here as an ignored test rather than left uncovered,
/// consistent with the already-flagged Byte()-array-to-string gap in <see cref="BinaryConcatOperatorRuntimeTests"/>.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.6 Let-coercion to and from a resizable Byte array")]
public sealed class VBResizableByteArrayLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static VBResizableByteArrayLetCoercionRuntimeSemantics Sut() => new(FakeProvider(), Formatter());

    [TestMethod]
    [Ignore("VBResizableByteArrayLetCoercionRuntimeSemantics.EvaluateLetCoercion isn't implemented yet (unconditionally throws NotImplementedException).")]
    public void StringSource_CoercesToByteArray()
        => AssertCoercedTo<VBResizableByteArrayValue>(Coerce(Sut(), new VBStringValue("x"), VBResizableByteArrayType.TypeInfo), (byte)'x');
}
