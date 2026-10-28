using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// The scalar types a payload is of, as compiled code names them: a constant an operation is told, rather than a <c>VBType</c> it tests.
/// </summary>
/// <remarks>
/// An integer type, a <c>Boolean</c> and a <c>Currency</c> are held as a <see cref="long"/> - a <c>Boolean</c> as its -1 or 0, a <c>Currency</c> as its
/// ten-thousandths; a <c>Single</c>, a <c>Double</c> and a <c>Date</c> as a <see cref="double"/>, a <c>Date</c> as its serial.
/// </remarks>
public enum ScalarKind
{
    /// <summary><c>Byte</c>.</summary>
    Byte,

    /// <summary><c>Integer</c>.</summary>
    Integer,

    /// <summary><c>Long</c>.</summary>
    Long,

    /// <summary><c>LongLong</c>.</summary>
    LongLong,

    /// <summary><c>Boolean</c>: -1 or 0.</summary>
    Boolean,

    /// <summary><c>Currency</c>: its ten-thousandths.</summary>
    Currency,

    /// <summary><c>Single</c>.</summary>
    Single,

    /// <summary><c>Double</c>.</summary>
    Double,

    /// <summary><c>Date</c>: its serial.</summary>
    Date,
}

/// <summary>
/// What a <see cref="ScalarKind"/> is of a type, and the type it is of.
/// </summary>
internal static class ScalarKinds
{
    /// <summary>The kind of <paramref name="type"/>, when it is a scalar type.</summary>
    public static ScalarKind? Of(VBType? type) => type switch
    {
        VBByteType => ScalarKind.Byte,
        VBIntegerType => ScalarKind.Integer,
        VBLongType => ScalarKind.Long,
        VBLongLongType => ScalarKind.LongLong,
        VBBooleanType => ScalarKind.Boolean,
        VBCurrencyType => ScalarKind.Currency,
        VBSingleType => ScalarKind.Single,
        VBDoubleType => ScalarKind.Double,
        VBDateType => ScalarKind.Date,
        _ => null,
    };

    /// <summary>The type of a kind.</summary>
    public static VBType TypeOf(ScalarKind kind) => kind switch
    {
        ScalarKind.Byte => VBByteType.TypeInfo,
        ScalarKind.Integer => VBIntegerType.TypeInfo,
        ScalarKind.Long => VBLongType.TypeInfo,
        ScalarKind.LongLong => VBLongLongType.TypeInfo,
        ScalarKind.Boolean => VBBooleanType.TypeInfo,
        ScalarKind.Currency => VBCurrencyType.TypeInfo,
        ScalarKind.Single => VBSingleType.TypeInfo,
        ScalarKind.Double => VBDoubleType.TypeInfo,
        _ => VBDateType.TypeInfo,
    };

    /// <summary>Whether a value of the kind is held as a <see cref="long"/>: an integer type, a <c>Boolean</c> or a <c>Currency</c>.</summary>
    public static bool IsIntegral(ScalarKind kind) => kind is not (ScalarKind.Single or ScalarKind.Double or ScalarKind.Date);
}
