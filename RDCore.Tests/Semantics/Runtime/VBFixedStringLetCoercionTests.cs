using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for FixedString let-coercion (MS-VBAL §5.5.1.2.5). The strategy is not
/// implemented yet (<c>EvaluateLetCoercion</c> unconditionally throws) — tracked here as an ignored
/// test rather than left uncovered, so the gap surfaces once the strategy is implemented.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.5 Let-coercion to and from FixedString")]
public sealed class VBFixedStringLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static VBFixedStringLetCoercionRuntimeSemantics Sut() => new(Formatter());

    [TestMethod]
    [Ignore("VBFixedStringLetCoercionRuntimeSemantics.EvaluateLetCoercion isn't implemented yet (unconditionally throws NotImplementedException).")]
    public void NumericSource_CoercesToFixedString()
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBLongValue(5), new VBFixedStringType(10)), "5         ");
}
