using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.SDK.Model.Values.Runtime;

/// <summary>
/// <strong>MS-VBAL 6.1.1.16 VbVarType</strong> — the COM Automation <c>VARENUM</c> tag space a
/// <c>Variant</c> value carries. These are the same numeric values the <c>VarType()</c> standard
/// library function reports and that OLE Automation marshals a <c>VARIANT</c> against, so RD-VBA's own
/// <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> stays shaped correctly for COM
/// interop even where a real host/marshalling layer doesn't exist yet.
/// </summary>
[StdLibEnum]
public enum VBVarType
{
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBEmptyType"/>.
    /// </summary>
    VBEmpty = 0,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBNullType"/>.
    /// </summary>
    VBNull = 1,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBIntegerType"/>.
    /// </summary>
    VBInteger = 2,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBLongType"/>.
    /// </summary>
    VBLong = 3,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBSingleType"/>.
    /// </summary>
    VBSingle = 4,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBDoubleType"/>.
    /// </summary>
    VBDouble = 5,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBCurrencyType"/>.
    /// </summary>
    VBCurrency = 6,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBDateType"/>.
    /// </summary>
    VBDate = 7,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBStringType"/>
    /// (a <c>BSTR</c> in the underlying COM representation).
    /// </summary>
    VBString = 8,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBObjectType"/>
    /// — COM's own <c>VT_DISPATCH</c>: a late-bound Automation object reference (an <c>IDispatch*</c>
    /// in the underlying COM representation).
    /// </summary>
    VBObject = 9,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBErrorType"/>
    /// — also the subtype of the <c>Missing</c> data value an omitted <c>Variant</c> argument carries
    /// (<c>DISP_E_PARAMNOTFOUND</c> in the underlying COM representation).
    /// </summary>
    VBError = 10,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBBooleanType"/>.
    /// </summary>
    VBBoolean = 11,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBVariantType"/>.
    /// </summary>
    /// <remarks>
    /// 👉 Variant <em>unwrapping</em> can get <em>really funky</em>.
    /// </remarks>
    VBVariant = 12,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBObjectType"/>
    /// — COM's own <c>VT_UNKNOWN</c>: an early-bound (<c>IUnknown</c>-only, non-Automation) object
    /// reference. RD-VBA does not yet distinguish this from <see cref="VBObject"/>; nothing constructs
    /// this value today.
    /// </summary>
    VBDataObject = 13,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBDecimalType"/>.
    /// </summary>
    VBDecimal = 14,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBByteType"/>.
    /// </summary>
    VBByte = 17,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is <see cref="VBLongLongType"/>
    /// (and, on a 64-bit host, <see cref="VBLongPtrType_x64"/>).
    /// </summary>
    /// <remarks>
    /// 👉 This value is statically undefined in a 32-bit environment.
    /// </remarks>
    VBLongLong = 20,
    /// <summary>
    /// The <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is a
    /// <see cref="RDCore.SDK.Model.Values.Intrinsic.VBUserDefinedTypeValue"/>.
    /// </summary>
    VBUserDefinedType = 36,
    /// <summary>
    /// Combined (bitwise-or'd) with the element type's own <see cref="VBVarType"/>: the
    /// <see cref="RDCore.SDK.Model.Values.Intrinsic.VBVariantValue"/> subtype is an array whose element
    /// type is that combined tag (MS-VBAL 6.1.1.16: "Any Array type: 8192 + VarType of element's type").
    /// </summary>
    VBArray = 8192,
}
