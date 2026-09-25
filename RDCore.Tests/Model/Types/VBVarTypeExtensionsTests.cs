using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Tests.Model.Types;

/// <summary>
/// <see cref="VBVarTypeExtensions.VarType"/> maps a declared <see cref="VBType"/> to the COM
/// <c>VARENUM</c>-compatible <see cref="VBVarType"/> tag a <c>Variant</c> wrapping it carries
/// (MS-VBAL 6.1.1.16), the same value <c>VarType()</c> would report.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.1.16 VbVarType")]
public sealed class VBVarTypeExtensionsTests
{
    [TestMethod]
    [DataRow(typeof(VBEmptyType), VBVarType.VBEmpty)]
    [DataRow(typeof(VBNullType), VBVarType.VBNull)]
    [DataRow(typeof(VBIntegerType), VBVarType.VBInteger)]
    [DataRow(typeof(VBLongType), VBVarType.VBLong)]
    [DataRow(typeof(VBSingleType), VBVarType.VBSingle)]
    [DataRow(typeof(VBDoubleType), VBVarType.VBDouble)]
    [DataRow(typeof(VBCurrencyType), VBVarType.VBCurrency)]
    [DataRow(typeof(VBDateType), VBVarType.VBDate)]
    [DataRow(typeof(VBStringType), VBVarType.VBString)]
    [DataRow(typeof(VBBooleanType), VBVarType.VBBoolean)]
    [DataRow(typeof(VBVariantType), VBVarType.VBVariant)]
    [DataRow(typeof(VBDecimalType), VBVarType.VBDecimal)]
    [DataRow(typeof(VBByteType), VBVarType.VBByte)]
    [DataRow(typeof(VBLongLongType), VBVarType.VBLongLong)]
    [DataRow(typeof(VBErrorType), VBVarType.VBError)]
    [DataRow(typeof(VBMissingType), VBVarType.VBError)]
    [DataRow(typeof(VBObjectType), VBVarType.VBObject)]
    public void VarType_OfAnIntrinsicType_ReportsItsOwnComVarEnumTag(Type vbType, VBVarType expected)
    {
        var typeInfo = (VBType)vbType.GetProperty("TypeInfo")!.GetValue(null)!;

        Assert.AreEqual(expected, typeInfo.VarType());
    }

    [TestMethod]
    public void VarType_OfAFixedString_IsTheSameAsString()
        // VBFixedStringType derives from VBStringType - no separate case should be needed.
        => Assert.AreEqual(VBVarType.VBString, new VBFixedStringType(5).VarType());

    [TestMethod]
    public void VarType_OfLongPtr_x64_IsLongLong()
        => Assert.AreEqual(VBVarType.VBLongLong, VBLongPtrType_x64.TypeInfo.VarType());

    [TestMethod]
    public void VarType_OfLongPtr_x86_IsLong()
        => Assert.AreEqual(VBVarType.VBLong, VBLongPtrType_x86.TypeInfo.VarType());

    [TestMethod]
    public void VarType_OfAnArrayOfLong_IsArrayCombinedWithLong()
        // MS-VBAL 6.1.1.16: "Any Array type: 8192 + VarType of element's type".
        => Assert.AreEqual(VBVarType.VBArray | VBVarType.VBLong, new VBFixedSizeArrayType(VBLongType.TypeInfo).VarType());

    [TestMethod]
    public void VarType_OfAnArrayOfVariant_IsArrayCombinedWithVariant()
        => Assert.AreEqual(VBVarType.VBArray | VBVarType.VBVariant, VBFixedSizeArrayType.TypeInfo.VarType());

    [TestMethod]
    public void VarType_OfAnArrayOfArrays_CombinesRecursively()
        => Assert.AreEqual(VBVarType.VBArray | VBVarType.VBArray | VBVarType.VBByte,
            new VBFixedSizeArrayType(new VBFixedSizeArrayType(VBByteType.TypeInfo)).VarType());

    [TestMethod]
    public void VarType_OfAUserDefinedType_IsUserDefinedType()
    {
        var uri = new Uri("file://rdcore-test#Point");
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, "Point", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var udt = new VBUserDefinedType(symbol, []);

        Assert.AreEqual(VBVarType.VBUserDefinedType, udt.VarType());
    }

    [TestMethod]
    public void VarType_OfAClass_DefaultsToObject_Dispatch()
        // every RD-VBA class module is Automation-capable by default (AutomationKind.Dispatch).
    {
        var root = new Uri("file://rdcore-test");
        var widget = new VBClassModuleSymbol(root, root, "Widget");

        Assert.AreEqual(VBVarType.VBObject, new VBClassType(widget, []).VarType());
    }

    [TestMethod]
    public void VarType_OfAnIUnknownOnlyClass_IsDataObject()
        // groundwork for a future external/COM reference kind: a class module explicitly marked
        // IUnknown-only reports VT_UNKNOWN (VbVarType's own vbDataObject, tag 13) instead of VT_DISPATCH.
    {
        var root = new Uri("file://rdcore-test");
        var widget = new VBClassModuleSymbol(root, root, "Widget") { AutomationKind = VBAutomationKind.Unknown };

        Assert.AreEqual(VBVarType.VBDataObject, new VBClassType(widget, []).VarType());
    }

    [TestMethod]
    public void VarType_OfAGenericObjectReference_DefaultsToObject_Dispatch()
        // VBObjectType (not VBClassType) is the shape a live VBObjectValue's own TypeInfo always has -
        // its concrete class is only known by looking up the actual instance, which this static mapping
        // has no access to; Dispatch is the only sound default absent that lookup.
        => Assert.AreEqual(VBVarType.VBObject, VBObjectType.TypeInfo.VarType());
}
