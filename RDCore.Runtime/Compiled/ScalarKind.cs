using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;

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
    /// <summary>
    /// The kind of <paramref name="type"/>, when it is a scalar type. An enumeration is a <c>Long</c> (<strong>MS-VBAL §5.2.3.4</strong>): what a variable
    /// declared as one holds is a <c>Long</c>, held in a cell of the <c>Long</c> type (<see cref="VBEnumType.AsDeclaredType"/>). A <c>LongPtr</c> is the kind
    /// of the type it is an alias of (<strong>MS-VBAL §2.2</strong>): a <c>LongLong</c> in a 64-bit environment and a <c>Long</c> in a 32-bit one, as the
    /// declaration was resolved for the session's environment; a variable declared as one holds it in a cell of its declared type.
    /// </summary>
    /// <remarks>
    /// The shared instance of each type is tested first, by reference (<see cref="OfShared"/>): it is the type of nearly every payload. Then the sealed
    /// types, each by its exact type, and the two <c>LongPtr</c> types last: neither is sealed, and a test of one looks through the whole chain of base
    /// types of a type that is not it.
    /// </remarks>
    /// <param name="type">The type, or <see langword="null"/>, which is of no kind.</param>
    public static ScalarKind? Of(VBType? type) => OfShared(type) ?? OfAny(type);

    /// <summary>
    /// The kind of <paramref name="type"/> when it is the shared instance of a scalar type (the one <see cref="TypeOf"/> gives); <see langword="null"/> of
    /// any other instance, an enumeration's and a <c>LongPtr</c>'s included.
    /// </summary>
    /// <param name="type">The type, compared by reference with each shared instance.</param>
    public static ScalarKind? OfShared(VBType? type)
        => ReferenceEquals(type, _long) ? ScalarKind.Long
            : ReferenceEquals(type, _double) ? ScalarKind.Double
            : ReferenceEquals(type, _integer) ? ScalarKind.Integer
            : ReferenceEquals(type, _boolean) ? ScalarKind.Boolean
            : ReferenceEquals(type, _longLong) ? ScalarKind.LongLong
            : ReferenceEquals(type, _byte) ? ScalarKind.Byte
            : ReferenceEquals(type, _currency) ? ScalarKind.Currency
            : ReferenceEquals(type, _single) ? ScalarKind.Single
            : ReferenceEquals(type, _date) ? ScalarKind.Date
            : null;

    // the shared instances, read once: each TypeInfo is a lazy value, which costs a call each time it is read.
    private static readonly VBType _byte = VBByteType.TypeInfo;
    private static readonly VBType _integer = VBIntegerType.TypeInfo;
    private static readonly VBType _long = VBLongType.TypeInfo;
    private static readonly VBType _longLong = VBLongLongType.TypeInfo;
    private static readonly VBType _boolean = VBBooleanType.TypeInfo;
    private static readonly VBType _currency = VBCurrencyType.TypeInfo;
    private static readonly VBType _single = VBSingleType.TypeInfo;
    private static readonly VBType _double = VBDoubleType.TypeInfo;
    private static readonly VBType _date = VBDateType.TypeInfo;

    private static ScalarKind? OfAny(VBType? type) => type switch
    {
        VBLongType => ScalarKind.Long,
        VBDoubleType => ScalarKind.Double,
        VBIntegerType => ScalarKind.Integer,
        VBBooleanType => ScalarKind.Boolean,
        VBLongLongType => ScalarKind.LongLong,
        VBByteType => ScalarKind.Byte,
        VBCurrencyType => ScalarKind.Currency,
        VBSingleType => ScalarKind.Single,
        VBDateType => ScalarKind.Date,
        VBEnumType => ScalarKind.Long,
        VBLongPtrType_x64 => ScalarKind.LongLong,
        VBLongPtrType_x86 => ScalarKind.Long,
        _ => null,
    };

    /// <summary>The type of a kind.</summary>
    public static VBType TypeOf(ScalarKind kind) => kind switch
    {
        ScalarKind.Byte => _byte,
        ScalarKind.Integer => _integer,
        ScalarKind.Long => _long,
        ScalarKind.LongLong => _longLong,
        ScalarKind.Boolean => _boolean,
        ScalarKind.Currency => _currency,
        ScalarKind.Single => _single,
        ScalarKind.Double => _double,
        _ => _date,
    };

    /// <summary>Whether a value of the kind is held as a <see cref="long"/>: an integer type, a <c>Boolean</c> or a <c>Currency</c>.</summary>
    public static bool IsIntegral(ScalarKind kind) => kind is not (ScalarKind.Single or ScalarKind.Double or ScalarKind.Date);
}
