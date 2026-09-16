using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for Null let-coercion (MS-VBAL §5.5.1.2.10): coercing <c>Null</c> to a
/// resizable array or UDT is a type mismatch; to anything else except <c>Null</c>, a fixed-size array
/// or Variant (all of which defer) it's an invalid use of Null.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.10 Let-coercion from Null")]
public sealed class VBNullTypeLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static VBNullTypeLetCoercionRuntimeSemantics Sut() => new(Formatter());

    [TestMethod]
    public void NullSource_CoercesToResizableArrayTarget_IsTypeMismatch()
        => AssertError(Coerce(Sut(), VBNullValue.Null, VBResizableArrayType.TypeInfo), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void NullSource_CoercesToUserDefinedTypeTarget_IsTypeMismatch()
        => AssertError(Coerce(Sut(), VBNullValue.Null, Udt("Foo")), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void NullSource_CoercesToOtherTarget_IsInvalidUseOfNull()
        => AssertError(Coerce(Sut(), VBNullValue.Null, VBLongType.TypeInfo), VBRuntimeErrorId.InvalidUseOfNull);

    private static VBUserDefinedType Udt(string name)
    {
        var uri = TestUri.TestModuleUri();
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, name, ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBUserDefinedType(symbol, []);
    }
}
