using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// Computes a <see cref="VBVarType"/> tag from a declared <see cref="VBType"/> — the mapping a
/// <c>Variant</c> value uses to stay correctly shaped for COM interop (MS-VBAL 6.1.1.16).
/// </summary>
public static class VBVarTypeExtensions
{
    /// <summary>
    /// Gets the <see cref="VBVarType"/> tag for a value of this declared type — the same tag
    /// <c>VarType()</c> would report for it. An array's own tag is <see cref="VBVarType.VBArray"/>
    /// combined with its element type's own tag, recursively (MS-VBAL 6.1.1.16's own "8192 + VarType of
    /// element's type"). A <see cref="VBClassType"/> defers to its own class module's
    /// <see cref="VBClassModuleSymbol.AutomationKind"/> (<see cref="VarType(VBClassModuleSymbol)"/>); a
    /// generic <see cref="VBObjectType"/> — a live object reference whose concrete class isn't known
    /// here, only ever resolvable by looking up the actual instance — defaults to
    /// <see cref="VBVarType.VBObject"/> (Automation-capable), the only sound default absent that lookup.
    /// </summary>
    public static VBVarType VarType(this VBType type) => type switch
    {
        VBEmptyType => VBVarType.VBEmpty,
        VBNullType => VBVarType.VBNull,
        VBIntegerType => VBVarType.VBInteger,
        VBLongType => VBVarType.VBLong,
        VBSingleType => VBVarType.VBSingle,
        VBDoubleType => VBVarType.VBDouble,
        VBCurrencyType => VBVarType.VBCurrency,
        VBDateType => VBVarType.VBDate,
        VBStringType => VBVarType.VBString, // covers VBFixedStringType too (derives from it)
        VBBooleanType => VBVarType.VBBoolean,
        VBVariantType => VBVarType.VBVariant,
        VBDecimalType => VBVarType.VBDecimal,
        VBByteType => VBVarType.VBByte,
        VBLongLongType => VBVarType.VBLongLong,
        VBLongPtrType_x64 => VBVarType.VBLongLong,
        VBLongPtrType_x86 => VBVarType.VBLong,
        // Missing is a VT_ERROR Variant carrying DISP_E_PARAMNOTFOUND (MS-VBAL 6.1.2.7.1.6's own remark).
        VBMissingType => VBVarType.VBError,
        VBErrorType => VBVarType.VBError,
        VBUserDefinedType => VBVarType.VBUserDefinedType,
        VBArrayType array => VBVarType.VBArray | array.ItemType.VarType(),
        VBClassType classType => classType.Symbol.VarType(),
        VBObjectType => VBVarType.VBObject,
        _ => VBVarType.VBEmpty,
    };

    /// <summary>
    /// Gets the <see cref="VBVarType"/> tag for a live instance of this class:
    /// <see cref="VBVarType.VBObject"/> (<c>VT_DISPATCH</c>) when
    /// <see cref="VBClassModuleSymbol.AutomationKind"/> is <see cref="VBAutomationKind.Dispatch"/> — every
    /// RD-VBA class module today — <see cref="VBVarType.VBDataObject"/> (<c>VT_UNKNOWN</c>) otherwise.
    /// </summary>
    public static VBVarType VarType(this VBClassModuleSymbol classModule) => classModule.AutomationKind switch
    {
        VBAutomationKind.Dispatch => VBVarType.VBObject,
        _ => VBVarType.VBDataObject,
    };
}
