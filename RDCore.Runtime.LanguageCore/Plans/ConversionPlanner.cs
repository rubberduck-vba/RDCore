using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using System.Collections.Concurrent;

namespace RDCore.Runtime.LanguageCore.Plans;

// MS-VBAL §5.5.1.2
public static class ConversionPlanner
{
    private static readonly ConcurrentDictionary<(VBType Source, VBType Destination), ConversionPlan> _plans = new();
    private static readonly RecentPlans<ConversionPlan> _recent = new();

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
