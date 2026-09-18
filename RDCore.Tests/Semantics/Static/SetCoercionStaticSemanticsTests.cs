using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static;
using RDCore.Tests.Semantics.Abstract;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// Characterization matrix for the MS-VBAL §5.5.2.1 static Set-coercion legality table: whether a
/// Set-coercion between a source and destination declared type is even statically possible, before
/// any runtime semantics (§5.5.2.2) — same-class/implements compatibility included — come into play.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.2.1 Set-coercion (static semantics)")]
public sealed class SetCoercionStaticSemanticsTests : StaticSemanticsTests
{
    private static readonly SetCoercionStaticSemantics Sut = new();

    private static void AssertValid(VBType source, VBType destination)
        => AssertDeterminedDeclaredType(Sut, [source, destination], destination);

    private static void AssertInvalid(VBType source, VBType destination)
        => AssertDeterminedDeclaredTypeIsError(Sut, [source, destination]);

    private static VBClassType Class(string name) => new(new VBClassModuleSymbol(TestUri.TestModuleUri(name), TestUri.TestModuleUri(name), name), []);

    [TestMethod]
    public void ClassSource_DifferentClassDestination_IsValid()
        // MS-VBAL 5.5.2.1's static table doesn't check class compatibility at all - only that both
        // sides are object-ish. Same-or-derived-class is a RUNTIME concern (5.5.2.2.1, error 13).
        => AssertValid(Class("Widget"), Class("Gadget"));

    [TestMethod]
    public void ObjectSource_ObjectDestination_IsValid()
        => AssertValid(VBObjectType.TypeInfo, VBObjectType.TypeInfo);

    [TestMethod]
    public void VariantSource_VariantDestination_IsValid()
        => AssertValid(VBVariantType.TypeInfo, VBVariantType.TypeInfo);

    [TestMethod]
    public void ClassSource_ObjectDestination_IsValid()
        => AssertValid(Class("Widget"), VBObjectType.TypeInfo);

    [TestMethod]
    public void ObjectSource_ClassDestination_IsValid()
        => AssertValid(VBObjectType.TypeInfo, Class("Widget"));

    [TestMethod]
    public void VariantSource_ClassDestination_IsValid()
        => AssertValid(VBVariantType.TypeInfo, Class("Widget"));

    [TestMethod]
    public void ClassSource_VariantDestination_IsValid()
        => AssertValid(Class("Widget"), VBVariantType.TypeInfo);

    [TestMethod]
    public void IntrinsicSource_ClassDestination_IsInvalid()
        => AssertInvalid(VBLongType.TypeInfo, Class("Widget"));

    [TestMethod]
    public void ClassSource_IntrinsicDestination_IsInvalid()
        => AssertInvalid(Class("Widget"), VBLongType.TypeInfo);

    [TestMethod]
    public void IntrinsicSource_IntrinsicDestination_IsInvalid()
        => AssertInvalid(VBLongType.TypeInfo, VBLongType.TypeInfo);

    [TestMethod]
    public void UnknownSource_IsDeferred_NotAnError()
        => AssertValid(VBUnknownType.TypeInfo, VBLongType.TypeInfo);

    [TestMethod]
    public void UnknownDestination_IsDeferred_NotAnError()
        => AssertValid(VBLongType.TypeInfo, VBUnknownType.TypeInfo);
}
