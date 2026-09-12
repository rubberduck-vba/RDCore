using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for Object let-coercion (MS-VBAL §5.5.1.2.13): an unset object reference
/// (<c>Nothing</c>) is a distinct runtime error from a non-object source. The default-member
/// resolution path (<c>GetObjectSimpleDataValue</c>) is a genuine, acknowledged-in-code unimplemented
/// gap (it unconditionally returns <c>default</c>) and is deliberately not exercised here.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.13 Let-coercion to and from Object")]
public sealed class VBObjectLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static VBObjectLetCoercionRuntimeSemantics Sut() => new(FakeProvider(), Formatter());

    [TestMethod]
    public void NothingSource_IsObjectVariableOrWithBlockVariableNotSet()
        => AssertError(Coerce(Sut(), VBObjectValue.Nothing, VBObjectType.TypeInfo), VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet);

    [TestMethod]
    public void NonObjectSource_IsObjectRequired()
        => AssertError(Coerce(Sut(), new VBLongValue(5), VBObjectType.TypeInfo), VBRuntimeErrorId.ObjectRequired);
}
