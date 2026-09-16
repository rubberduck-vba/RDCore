using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for ResizableArray let-coercion (MS-VBAL §5.5.1.2.7). The value-producing
/// half of the strategy is not implemented yet (<c>EvaluateLetCoercion</c> unconditionally throws) —
/// tracked here as an ignored test. The semantic-analysis half is implemented and exercised directly.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.7 Let-coercion to and from a resizable array")]
public sealed class VBResizableArrayLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static VBResizableArrayLetCoercionRuntimeSemantics Sut() => new(FakeProvider(), Formatter());

    [TestMethod]
    [Ignore("VBResizableArrayLetCoercionRuntimeSemantics.EvaluateLetCoercion isn't implemented yet (unconditionally throws NotImplementedException).")]
    public void NumericSource_IntoArrayTarget_IsTypeMismatch()
        => AssertError(Coerce(Sut(), new VBLongValue(5), VBResizableArrayType.TypeInfo), RDCore.SDK.Model.Errors.VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void Analyze_AddsArrayTargetFlag()
    {
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, new VBLongValue(5), new VBTypeDescValue(VBResizableArrayType.TypeInfo));
        var builder = new LetCoercionSemanticContextFlagsBuilder();

        Sut().Analyze(builder, null!, ThrowawayExpression, frame, LetCoercionResult.NotApplicable(frame));

        Assert.IsTrue(builder.Flags.HasFlag(ConversionSemanticFlags.ArrayTarget));
    }
}
