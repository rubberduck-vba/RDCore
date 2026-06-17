using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.1.16 VbVarType</strong>
/// </summary>
/// <remarks>
/// These values encode the possible return values of the <see cref="IStdInformationModule.StdInformation__VarType"/> function.
/// </remarks>
public enum VBVarType
{
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBEmptyType"/>.
    /// </summary>
    VBEmpty = 0,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBNullType"/>.
    /// </summary>
    VBNull = 1,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBIntegerType"/>.
    /// </summary>
    VBInteger = 2,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBLongType"/>.
    /// </summary>
    VBLong = 3,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBSingleType"/>.
    /// </summary>
    VBSingle = 4,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBDoubleType"/>.
    /// </summary>
    VBDouble = 5,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBCurrencyType"/>.
    /// </summary>
    VBCurrency = 6,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBDateType"/>.
    /// </summary>
    VBDate = 7,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBStringType"/>.
    /// </summary>
    VBString = 8,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBObjectType"/>.
    /// </summary>
    VBObject = 9,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBErrorType"/>.
    /// </summary>
    VBError = 10,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBBooleanType"/>.
    /// </summary>
    VBBoolean = 11,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBVariantType"/>.
    /// </summary>
    /// <remarks>
    /// 👉 Variant <em>unwrapping</em> can get <em>really funky</em>.
    /// </remarks>
    VBVariant = 12,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBObjectType"/>.
    /// </summary>
    VBDataObject = 13,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBDecimalType"/>.
    /// </summary>
    VBDecimal = 14,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBByteType"/>.
    /// </summary>
    VBByte = 17,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBLongLongType"/>.
    /// </summary>
    /// <remarks>
    /// 👉 This value is statically undefined in a 32-bit environment.
    /// </remarks>
    VBLongLong = 20,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBUserDefinedTypeValue"/>.
    /// </summary>
    VBUserDefinedType = 36,
    /// <summary>
    /// The <see cref="VBVariantValue"/> subtype is <see cref="VBArrayType"/>.
    /// </summary>
    VBArray = 8192,
}

