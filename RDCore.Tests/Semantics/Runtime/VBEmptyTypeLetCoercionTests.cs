using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for Empty let-coercion (MS-VBAL §5.5.1.2.11): coercing <c>Empty</c> to a
/// numeric type, Boolean, Date or String yields that type's zero-ish default; to an object/class it's
/// Object required; to anything else (excluding Variant, which defers) it's a type mismatch.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.11 Let-coercion from Empty")]
public sealed class VBEmptyTypeLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static VBEmptyTypeLetCoercionRuntimeSemantics Sut() => new(Formatter());

    [TestMethod]
    public void EmptySource_CoercesToNumericTarget_IsZero()
        => AssertCoercedTo<VBLongValue>(Coerce(Sut(), VBEmptyValue.Empty, VBLongType.TypeInfo), 0);

    [TestMethod]
    public void EmptySource_CoercesToBooleanTarget_IsFalse()
        => AssertCoercedTo<VBBooleanValue>(Coerce(Sut(), VBEmptyValue.Empty, VBBooleanType.TypeInfo), false);

    [TestMethod]
    public void EmptySource_CoercesToDateTarget_IsTheZeroDate()
        => AssertCoercedTo<VBDateValue>(Coerce(Sut(), VBEmptyValue.Empty, VBDateType.TypeInfo), VBDateType.Zero.SerialValue);

    [TestMethod]
    public void EmptySource_CoercesToStringTarget_IsAZeroLengthString()
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), VBEmptyValue.Empty, VBStringType.TypeInfo), string.Empty);

    [TestMethod]
    public void EmptySource_CoercesToObjectTarget_IsObjectRequired()
        => AssertError(Coerce(Sut(), VBEmptyValue.Empty, VBObjectType.TypeInfo), VBRuntimeErrorId.ObjectRequired);

    [TestMethod]
    public void EmptySource_CoercesToNonVariantOtherTarget_IsTypeMismatch()
        // e.g. a resizable array target — anything that isn't numeric/Boolean/Date/String/String*length/
        // object/class/Variant falls to the general mismatch case.
        => AssertError(Coerce(Sut(), VBEmptyValue.Empty, VBResizableArrayType.TypeInfo), VBRuntimeErrorId.TypeMismatch);
}
