using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using System.Collections.Concurrent;

namespace RDCore.Runtime.LanguageCore.Plans;

/// <summary>
/// Makes the <see cref="ConversionPlan"/> of a let-coercion from the type of the value and the type it is let-coerced to: the one table of the rules of
/// <strong>MS-VBAL §5.5.1.2</strong>, consulted when a conversion is bound from declared types and when it is evaluated on a value (IR lowering §3.2).
/// </summary>
/// <remarks>
/// <para>
/// A plan depends on nothing but the two types, so each is made once and kept, and the plans last asked for are also kept by the identity of the types
/// (<see cref="RecentPlans{TPlan}"/>): a variable assigned in a loop is let-coerced from the same type every time.
/// </para>
/// <para>
/// An object, <c>Null</c>, <c>Empty</c> and an <c>Error</c> are let-coerced by rules of their own, whatever the destination is - but for a <c>Variant</c>
/// or a fixed-size array, which hold <c>Null</c>, <c>Empty</c> and an <c>Error</c> as they are, or have no rule for them. Anything else is let-coerced by
/// the rule of its destination. Whether that rule takes the value is the conversion's to say: the plan names the rule, and the rule decides.
/// </para>
/// <para>
/// The static semantics have a table of their own (<strong>§5.5.1.1</strong>), keyed by declared types, which says which let-coercions are valid at all, and
/// are not to consult this one: they are stricter where the specification makes them so - an implicit let-coercion from a LongLong to anything but a
/// LongLong or a Variant is invalid, though the same value let-coerced at run time from a Variant is not. What they accept, this table does not reject for
/// the same declared types - but where the specification itself makes a <c>Byte()</c> let-coerced to a number, a Boolean or a date statically valid
/// (§5.5.1.1 lists no such row) and a type mismatch whenever it runs (§5.5.1.2.6).
/// </para>
/// </remarks>
public static class ConversionPlanner
{
    private static readonly ConcurrentDictionary<(VBType Source, VBType Destination), ConversionPlan> _plans = new();
    private static readonly RecentPlans<ConversionPlan> _recent = new();

    /// <summary>
    /// The plan of the let-coercion of a value of this type: final, whatever the types are.
    /// </summary>
    /// <param name="source">
    /// The value type of the source: the type of what a Variant holds, and for an array, an array of its elements - a <c>Byte()</c> is let-coerced by rules
    /// an array of anything else is not.
    /// </param>
    /// <param name="destination">The declared type the value is let-coerced to.</param>
    public static ConversionPlan ForValue(VBType source, VBType destination)
    {
        if (_recent.TryGet(source, destination, null, out var plan))
        {
            return plan;
        }

        plan = _plans.GetOrAdd((source, destination), key => Plan(key.Source, key.Destination));
        _recent.Add(source, destination, null, plan);
        return plan;
    }

    /// <summary>
    /// The plan of the let-coercion of a source of this declared type: <see cref="ConversionPlan.IsDeferred"/> when it is declared <c>Variant</c> or is
    /// late-bound (<see cref="VBUnknownType"/>), whose value type is not known until the conversion is evaluated, and the plan of its value otherwise.
    /// </summary>
    /// <param name="source">The declared type of the source.</param>
    /// <param name="destination">The declared type the value is let-coerced to.</param>
    /// <remarks>
    /// A fixed-length string holds a <c>String</c>, an Enum a <c>Long</c>, a <c>LongPtr</c> what it is an alias of, and a class an object: those are the
    /// value types the plan is made for.
    /// </remarks>
    public static ConversionPlan ForDeclared(VBType source, VBType destination)
        => source is VBVariantType or VBUnknownType
            ? new ConversionPlan(source, DestinationOf(destination), ConversionRule.None, IsDeferred: true)
            : ForValue(ValueTypeOf(source), destination);

    private static VBType ValueTypeOf(VBType declared) => declared switch
    {
        VBFixedStringType => VBStringType.TypeInfo,
        VBEnumType => VBLongType.TypeInfo,
        VBClassType => VBObjectType.TypeInfo,
        _ => AliasedTypeOf(declared),
    };

    // MS-VBAL §2.2: LongPtr is an alias, of LongLong in a 64-bit environment and of Long in a 32-bit one.
    private static VBType AliasedTypeOf(VBType type) => type switch
    {
        VBLongPtrType_x64 => VBLongLongType.TypeInfo,
        VBLongPtrType_x86 => VBLongType.TypeInfo,
        _ => type,
    };

    // MS-VBAL §5.2.3.4: what is let-coerced to an enumeration is let-coerced to a Long.
    private static VBType DestinationOf(VBType declared) => declared is VBEnumType ? VBLongType.TypeInfo : AliasedTypeOf(declared);

    private static ConversionPlan Plan(VBType source, VBType declaredDestination)
    {
        source = AliasedTypeOf(source);
        var destination = DestinationOf(declaredDestination);
        var rule = source switch
        {
            VBObjectType => ConversionRule.FromObject,
            VBNullType when HasRulesForNullEmptyAndError(destination) => ConversionRule.FromNull,
            VBEmptyType when HasRulesForNullEmptyAndError(destination) => ConversionRule.FromEmpty,
            VBErrorType when HasRulesForNullEmptyAndError(destination) => ConversionRule.FromError,
            _ => RuleOf(destination),
        };

        return new ConversionPlan(source, destination, TakesTheSource(source, rule) ? rule : ConversionRule.None);
    }

    // MS-VBAL §5.5.1.2.6-8: an array and a user-defined type are let-coerced to few types, and to any other one they are a type mismatch. A Byte() is the
    // data of a String, and is let-coerced to one; an array of anything else, and a user-defined type, are not even that. What each takes of the rest -
    // an array of the same elements, the same user-defined type - is the rule's to say.
    private static bool TakesTheSource(VBType source, ConversionRule rule) => source switch
    {
        VBArrayType { ItemType: VBByteType } => rule is not (ConversionRule.Numeric or ConversionRule.Boolean or ConversionRule.Date),
        VBArrayType => rule is not (ConversionRule.Numeric or ConversionRule.Boolean or ConversionRule.Date
            or ConversionRule.String or ConversionRule.FixedString or ConversionRule.ByteArray),
        VBUserDefinedType => rule is not (ConversionRule.Numeric or ConversionRule.Boolean or ConversionRule.Date
            or ConversionRule.String or ConversionRule.FixedString or ConversionRule.ByteArray or ConversionRule.Array),
        _ => true,
    };

    // MS-VBAL §5.5.1.2.9-11: Null, Empty and an Error have rules of their own to any destination "except a fixed-size array or Variant".
    private static bool HasRulesForNullEmptyAndError(VBType destination) => destination is not VBVariantType and not VBFixedSizeArrayType;

    // the rule of each destination type, the more specific type first: a fixed-length string is a String, and a Byte() a resizable array.
    private static ConversionRule RuleOf(VBType destination) => destination switch
    {
        VBFixedStringType => ConversionRule.FixedString,
        VBStringType => ConversionRule.String,
        VBNumericType => ConversionRule.Numeric,
        VBBooleanType => ConversionRule.Boolean,
        VBDateType => ConversionRule.Date,
        VBVariantType => ConversionRule.Variant,
        VBObjectType => ConversionRule.Object,
        VBEmptyType => ConversionRule.Empty,
        VBNullType => ConversionRule.Null,
        VBErrorType => ConversionRule.Error,
        VBUserDefinedType => ConversionRule.UserDefinedType,
        VBResizableByteArrayType => ConversionRule.ByteArray,
        VBResizableArrayType => ConversionRule.Array,
        _ => ConversionRule.None,
    };
}
