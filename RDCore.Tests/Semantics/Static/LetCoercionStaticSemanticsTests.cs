using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static;
using RDCore.Tests.Semantics.Abstract;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// Characterization matrix for the MS-VBAL §5.5.1.1 static let-coercion legality table: whether a
/// let-coercion between a source and destination declared type is even statically possible, before
/// any runtime semantics (§5.5.1.2) come into play.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.1 Let-coercion (static semantics)")]
public sealed class LetCoercionStaticSemanticsTests : StaticSemanticsTests
{
    private static readonly LetCoercionStaticSemantics Sut = new();

    private static void AssertValid(VBType source, VBType destination)
        => AssertDeterminedDeclaredType(Sut, [source, destination], destination);

    private static void AssertInvalid(VBType source, VBType destination)
        => AssertDeterminedDeclaredTypeIsError(Sut, [source, destination]);

    private static VBUserDefinedType Udt(string name, bool external = false)
    {
        var uri = TestUri.TestModuleUri(name);
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, name, ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return external ? new VBExternalUserDefinedType(symbol, []) : new VBUserDefinedType(symbol, []);
    }

    private static VBClassType ClassWithDefaultMember(VBType? defaultMemberResolvedType)
    {
        var uri = TestUri.TestModuleUri("TestClass");
        var classSymbol = new VBClassModuleSymbol(uri, uri, "TestClass");
        var defaultMember = defaultMemberResolvedType is null
            ? null
            : new VBProcedureMemberSymbol(uri, uri, "Value", ScopeKind.Instance, SymbolKindExt.Property, defaultMemberResolvedType, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBClassType(classSymbol, []) { DefaultMember = defaultMember };
    }

    // --- catch-all: ordinary coercions that MS-VBAL's table doesn't declare invalid must be valid. ---
    // (a regression the size of a whole switch arm was found and fixed here: a stray arm matched
    // literally every source type for any non-ResizableByteArray destination, making nearly every
    // let-coercion in the language statically illegal — including as basic a case as Long -> Long.)

    [TestMethod]
    public void SameNumericType_IsValid()
        => AssertValid(VBLongType.TypeInfo, VBLongType.TypeInfo);

    [TestMethod]
    public void NumericWidening_IsValid()
        => AssertValid(VBIntegerType.TypeInfo, VBDoubleType.TypeInfo);

    [TestMethod]
    public void NumericNarrowing_IsValid()
        => AssertValid(VBLongType.TypeInfo, VBIntegerType.TypeInfo);

    [TestMethod]
    public void StringToString_IsValid()
        => AssertValid(VBStringType.TypeInfo, VBStringType.TypeInfo);

    [TestMethod]
    public void StringToVariant_IsValid()
        => AssertValid(VBStringType.TypeInfo, VBVariantType.TypeInfo);

    [TestMethod]
    public void NumericToVariant_IsValid()
        => AssertValid(VBLongType.TypeInfo, VBVariantType.TypeInfo);

    [TestMethod]
    public void DateToNumeric_IsValid()
        => AssertValid(VBDateType.TypeInfo, VBLongType.TypeInfo);

    [TestMethod]
    public void BooleanToNumeric_IsValid()
        => AssertValid(VBBooleanType.TypeInfo, VBLongType.TypeInfo);

    // --- "Any type" -> "Any fixed-size array" is invalid. ---

    [TestMethod]
    public void AnyType_ToFixedSizeArray_IsInvalid()
        => AssertInvalid(VBLongType.TypeInfo, new VBFixedSizeArrayType(VBLongType.TypeInfo));

    // --- "Any numeric type or Boolean or Date" -> "Resizable Byte()" is invalid. ---

    [TestMethod]
    public void NumericToResizableByteArray_IsInvalid()
        => AssertInvalid(VBLongType.TypeInfo, VBResizableByteArrayType.TypeInfo);

    [TestMethod]
    public void BooleanToResizableByteArray_IsInvalid()
        => AssertInvalid(VBBooleanType.TypeInfo, VBResizableByteArrayType.TypeInfo);

    [TestMethod]
    public void DateToResizableByteArray_IsInvalid()
        => AssertInvalid(VBDateType.TypeInfo, VBResizableByteArrayType.TypeInfo);

    [TestMethod]
    public void StringToResizableByteArray_IsValid()
        // String isn't excluded by the numeric/Boolean/Date rule above.
        => AssertValid(VBStringType.TypeInfo, VBResizableByteArrayType.TypeInfo);

    // --- "Any type except a non-Byte resizable or fixed-size array or Variant" -> "Any non-Byte
    // resizable array" is invalid. ---

    [TestMethod]
    public void NumericToNonByteResizableArray_IsInvalid()
        => AssertInvalid(VBLongType.TypeInfo, new VBResizableArrayType(VBLongType.TypeInfo));

    [TestMethod]
    public void VariantToNonByteResizableArray_IsValid()
        => AssertValid(VBVariantType.TypeInfo, new VBResizableArrayType(VBLongType.TypeInfo));

    [TestMethod]
    public void SameElementTypeArray_IsValid()
        => AssertValid(new VBFixedSizeArrayType(VBLongType.TypeInfo), new VBResizableArrayType(VBLongType.TypeInfo));

    [TestMethod]
    public void DifferentElementTypeArray_IsInvalid()
        => AssertInvalid(new VBResizableArrayType(VBLongType.TypeInfo), new VBResizableArrayType(VBStringType.TypeInfo));

    [TestMethod]
    public void ArrayToVariant_IsValid()
        // spec explicitly carves Variant out of the "non-array type" half of this rule — an array can
        // always coerce to Variant (subject to the separate UDT-array/fixed-string-array rules below).
        => AssertValid(new VBResizableArrayType(VBLongType.TypeInfo), VBVariantType.TypeInfo);

    [TestMethod]
    public void ByteArrayToVariant_IsValid()
        // VBResizableByteArrayType is explicitly excluded from the array-source rules above.
        => AssertValid(VBResizableByteArrayType.TypeInfo, VBVariantType.TypeInfo);

    // --- "Any type except a UDT or Variant" -> "Any UDT" is invalid. ---

    [TestMethod]
    public void NumericToUdt_IsInvalid()
        => AssertInvalid(VBLongType.TypeInfo, Udt("Foo"));

    [TestMethod]
    public void VariantToUdt_IsValid()
        => AssertValid(VBVariantType.TypeInfo, Udt("Foo"));

    // --- "Any type except Variant" -> "Any class or Object" is invalid (Let-coercion never performs
    // a Set/reference assignment). ---

    [TestMethod]
    public void NumericToObject_IsInvalid()
        => AssertInvalid(VBLongType.TypeInfo, VBObjectType.TypeInfo);

    [TestMethod]
    public void VariantToObject_IsValid()
        => AssertValid(VBVariantType.TypeInfo, VBObjectType.TypeInfo);

    // --- "Any class which has no accessible default Property Get or function, or which has one for
    // which it is statically invalid to let-coerce its declared type to the destination" is invalid.
    // This rule applies to a class source regardless of destination (unlike the Object/Class-destination
    // rule above, which fires first and independently for that specific destination). ---

    [TestMethod]
    public void ClassWithNoDefaultMember_ToNumeric_IsInvalid()
        => AssertInvalid(ClassWithDefaultMember(null), VBLongType.TypeInfo);

    [TestMethod]
    public void ClassWithValidDefaultMember_ToCompatibleNumeric_IsValid()
        => AssertValid(ClassWithDefaultMember(VBLongType.TypeInfo), VBLongType.TypeInfo);

    [TestMethod]
    public void ClassWithDefaultMember_ToIncompatibleDestination_IsInvalidRecursively()
        // the class's default member (Long) would itself be an invalid coercion source for a UDT
        // destination, per the UDT rule above — the class source inherits that same invalidity.
        => AssertInvalid(ClassWithDefaultMember(VBLongType.TypeInfo), Udt("Foo"));

    // --- "Any UDT" -> "a different UDT than source type, or any non-UDT type except Variant" is invalid. ---

    [TestMethod]
    public void SameUdt_IsValid()
    {
        var udt = Udt("Foo");
        AssertValid(udt, udt);
    }

    [TestMethod]
    public void DifferentUdt_IsInvalid()
        => AssertInvalid(Udt("Foo"), Udt("Bar"));

    [TestMethod]
    public void UdtToNonUdtNonVariant_IsInvalid()
        => AssertInvalid(Udt("Foo"), VBLongType.TypeInfo);

    // --- "A UDT not imported from an external reference" -> "Variant" is invalid; an external one is fine. ---

    [TestMethod]
    public void LocalUdtToVariant_IsInvalid()
        => AssertInvalid(Udt("Foo"), VBVariantType.TypeInfo);

    [TestMethod]
    public void ExternalUdtToVariant_IsValid()
        => AssertValid(Udt("Foo", external: true), VBVariantType.TypeInfo);

    // --- "An array of UDTs not imported from an external reference" -> "Variant" is invalid. ---

    [TestMethod]
    public void ArrayOfLocalUdtToVariant_IsInvalid()
        => AssertInvalid(new VBResizableArrayType(Udt("Foo")), VBVariantType.TypeInfo);

    [TestMethod]
    public void ArrayOfExternalUdtToVariant_IsValid()
        => AssertValid(new VBResizableArrayType(Udt("Foo", external: true)), VBVariantType.TypeInfo);

    // --- "An array of fixed-length strings" -> "Variant" is invalid. ---

    [TestMethod]
    public void ArrayOfFixedStringToVariant_IsInvalid()
        => AssertInvalid(new VBResizableArrayType(new VBFixedStringType(10)), VBVariantType.TypeInfo);

    // --- LongLong is only implicitly let-coercible to LongLong or Variant (specified outside the
    // §5.5.1.1 table itself; explicit CType coercion is the only way to narrow it further). ---

    [TestMethod]
    public void LongLongToInteger_IsInvalid()
        => AssertInvalid(VBLongLongType.TypeInfo, VBIntegerType.TypeInfo);

    [TestMethod]
    public void LongLongToLongLong_IsValid()
        => AssertValid(VBLongLongType.TypeInfo, VBLongLongType.TypeInfo);

    [TestMethod]
    public void LongLongToVariant_IsValid()
        => AssertValid(VBLongLongType.TypeInfo, VBVariantType.TypeInfo);
}
