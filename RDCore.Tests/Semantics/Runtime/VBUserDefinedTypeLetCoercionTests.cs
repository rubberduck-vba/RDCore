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
/// Characterization matrix for UDT let-coercion (MS-VBAL §5.5.1.2.8): the same UDT type is a shallow
/// (shared-reference) copy; any other declared type — including a different UDT, or any non-UDT
/// source coercing into a UDT — is a type mismatch.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.8 Let-coercion to and from a UDT")]
public sealed class VBUserDefinedTypeLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static VBUserDefinedTypeLetCoercionRuntimeSemantics Sut() => new(FakeProvider(), Formatter());

    // Symbol identity is location-based, not name-based — a distinct module name per UDT (via
    // TestModuleUri's own module-name parameter) gives each Udt(...) call a genuinely distinct type.
    // (RDCore.Tests.Model.Types.VBUserDefinedTypeTests covers VBUserDefinedType equality/ToString
    // directly, including the stack-overflow regression this used to hit.)
    private static VBUserDefinedType Udt(string name)
    {
        var uri = TestUri.TestModuleUri(name);
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, name, ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBUserDefinedType(symbol, []);
    }

    [TestMethod]
    public void SameUdtSource_IsASharedReferenceCopy()
    {
        var udtType = Udt("Foo");
        var source = new VBUserDefinedTypeValue(udtType);

        var result = Coerce(Sut(), source, udtType);

        Assert.IsTrue(result.IsApplicable);
        Assert.IsNull(result.ErrorInfo);
        Assert.IsInstanceOfType<VBUserDefinedTypeValue>(result.Result);
        Assert.AreEqual(source.Value, ((VBUserDefinedTypeValue)result.Result!).Value);
    }

    [TestMethod]
    public void UdtSourceIntoNonMatchingTarget_IsTypeMismatch()
        => AssertError(Coerce(Sut(), new VBUserDefinedTypeValue(Udt("Foo")), VBLongType.TypeInfo), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void DifferentUdtSource_IsTypeMismatch()
        => AssertError(Coerce(Sut(), new VBUserDefinedTypeValue(Udt("Foo")), Udt("Bar")), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void NumericSource_IntoUdtTarget_IsTypeMismatch()
        => AssertError(Coerce(Sut(), new VBLongValue(5), Udt("Foo")), VBRuntimeErrorId.TypeMismatch);
}
