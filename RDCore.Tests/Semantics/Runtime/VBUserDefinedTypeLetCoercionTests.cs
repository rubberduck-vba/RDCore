using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for UDT let-coercion (MS-VBAL §5.5.1.2.8): the same UDT type is a copy whose
/// fields are its own; any other declared type — including a different UDT, or any non-UDT
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

    // "a shallow copy of the UDT ... all other elements are Let-assigned": the copy's fields are its own, and a field written through one is not
    // written in the other.
    [TestMethod]
    public void SameUdtSource_IsACopyThatSharesNoField()
    {
        var uri = TestUri.TestModuleUri("Bar");
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, "Bar", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var field = new VBUserDefinedTypeFieldSymbol(uri, symbol.Uri, "X", VBLongType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var udtType = new VBUserDefinedType(symbol, [field]);
        var source = new VBUserDefinedTypeValue(udtType);
        ((IKnowledgeBinding)source.GetFieldHandle("X")!).Store(new VBLongValue(1).RuntimeValue, isKnown: true);

        var result = Coerce(Sut(), source, udtType);

        Assert.IsTrue(result.IsApplicable);
        Assert.IsNull(result.ErrorInfo);
        var copy = (VBUserDefinedTypeValue)result.Result!;
        Assert.AreEqual(1, ((VBLongValue)copy["X"]!).Value, "the copy holds what the source held");

        ((IKnowledgeBinding)copy.GetFieldHandle("X")!).Store(new VBLongValue(2).RuntimeValue, isKnown: true);
        Assert.AreEqual(1, ((VBLongValue)source["X"]!).Value, "a write to the copy's field is not a write to the source's");
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
