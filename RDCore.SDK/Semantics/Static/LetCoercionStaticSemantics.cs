using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// MS-VBAL 5.5.1.1 Let-coercion (static semantics)
/// </summary>
public record class LetCoercionStaticSemantics : StaticSemantics
{
    private static readonly Lazy<LetCoercionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static IStaticSemantics Instance => _instance.Value;

    public override StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        var destinationType = operandDeclaredTypes[(int)InputIndex.CoercionDestinationType];
        var sourceType = operandDeclaredTypes[(int)InputIndex.CoercionSourceValue];

        // what an object's class declares is read from the class as the composition has it now, not as it was when the declaration was bound.
        if (sourceType is VBClassType declaredClass)
        {
            sourceType = CurrentTypes.Of(context, declaredClass);
        }

        // MS-VBAL §3.3.5.2: the Empty and Null literals have a declared type of Variant, which is valid to Let-coerce to an array; the value each holds is
        // not, and raises 13 when it runs (MS-VBA, Excel-measured, oracle s46 F17a-b, s48 J01-J02). The fact is for an analyzer to report.
        if (sourceType is VBEmptyType or VBNullType && destinationType is VBArrayType)
        {
            if (context.Facts is { } facts && facts.TryGet(expression.Identity, out var fact))
            {
                facts.Record(fact with { Flags = fact.Flags | ValueExpressionSemanticFlags.EmptyLetToArray });
            }

            sourceType = VBVariantType.TypeInfo;
        }

        // an object of a class that has no default member has no value to give a Variant either: MS-VBA compiles it, and raises 438 when it runs (RD-VBAL 5.5.1.1).
        if (sourceType is VBClassType { DefaultMember: null } && destinationType is VBVariantType)
        {
            ObjectValueStaticSemantics.MarkNoDefaultMember(context.Facts, expression);
            return StaticSemanticsEvaluationResult.Success(destinationType);
        }

        // a destination of a value type expects a value of the source: an object stands for its default member's value, which it must have.
        if (ObjectValueStaticSemantics.IsValueType(destinationType))
        {
            var value = ObjectValueStaticSemantics.ValueOf(context, expression, sourceType, ValueDestination.TypedValue);
            if (value.IsError)
            {
                return value;
            }

            sourceType = value.Result!;
        }

        return !IsLetCoercionInvalid(sourceType, destinationType)
            ? StaticSemanticsEvaluationResult.Success(destinationType)
            : StaticSemanticsEvaluationResult.Error(GetStaticCoercionTypeMismatchErrorInfo(expression, operandDeclaredTypes));
    }

    /// <summary>
    /// NOTE: This section of the MS-VBAL specification is notoriously evil. 
    /// The mind-bending double-negative logic underlying this implementation is fully intentional.
    /// Readability tweaks welcome, but pattern-matching and helper functions are the only way to keep one's sanity here.
    /// </summary>
    /// <returns>
    /// <c>true</c> if a let-coercion operation is <strong>invalid</strong> between the specified source and destination data types.
    /// </returns>
    private static bool IsLetCoercionInvalid(VBType source, VBType destination) => source switch
    {
        VBType when destination is VBFixedSizeArrayType => true,
        INumericType or VBBooleanType or VBDateType when destination is VBResizableByteArrayType => true,
        VBType when AnyTypeExceptNonByteResizableArrayOr(source, typeof(VBFixedSizeArrayType), typeof(VBVariantType)) && IsNonByteResizableArray(destination) => true,
        VBType and not VBUserDefinedType and not VBVariantType when destination is VBUserDefinedType => true,
        VBType and not VBVariantType when destination is VBObjectType or VBClassType => true,
        // a class to a value type is its default member's value, which ObjectValueStaticSemantics checks first, saying what MS-VBA does where it differs.
        VBClassType type when type.DefaultMember is null || IsLetCoercionInvalid(type.DefaultMember.ResolvedType, destination) => true,
        VBFixedSizeArrayType or VBResizableArrayType and not VBResizableByteArrayType
            when destination is not VBVariantType && (destination is not VBArrayType
                || destination is VBResizableArrayType dstArray && source is VBArrayType srcArray && !IsSameItemType(dstArray.ItemType, srcArray.ItemType)) => true,
        VBUserDefinedType srcUdt when destination is not VBVariantType
            && (destination is not VBUserDefinedType || destination is VBUserDefinedType dstUdt && !srcUdt.Equals(dstUdt)) => true,
        VBUserDefinedType and not VBExternalUserDefinedType when destination is VBVariantType => true,
        VBArrayType srcArray when srcArray.ItemType is VBUserDefinedType and not VBExternalUserDefinedType && destination is VBVariantType => true,
        VBArrayType srcArray when srcArray.ItemType is VBFixedStringType && destination is VBVariantType => true,

        // MS-VBAL §5.5.1.1 (below its table) refuses to let-coerce a LongLong implicitly to anything but a LongLong or a Variant. MS-VBA refuses only what
        // is narrower than it - Byte, Integer, Long (an Enum is one, §5.2.3.4), Currency - and lets Double, Single, String, Boolean and Date have it
        // (Excel-verified); RD-VBA does what MS-VBA does (author's ruling, 2026-10-22). A 64-bit LongPtr is a LongLong (§2.2), and a 32-bit one a Long.
        VBLongLongType or VBLongPtrType_x64
            when destination is VBByteType or VBIntegerType or VBLongType or VBEnumType or VBCurrencyType or VBLongPtrType_x86 => true,

        // MS-VBAL 5.5.1.1's "source declared type OR LITERAL": the Nothing literal is told by its expression, not by a type, so DetermineDeclaredType asks
        // ObjectValueStaticSemantics, which reports it as MS-VBA does (Invalid use of object) before these rules are consulted.

        _ => false
    };

    // the element types of two arrays are the same type when they are: a class is the same class when it is the same class module, whichever of
    // the records that describe it each array was declared with - one built when the module's symbols were read, another when a declaration's
    // type was resolved - and which differ in what they carry (its members) while naming the one class.
    private static bool IsSameItemType(VBType left, VBType right)
        => left is VBClassType leftClass && right is VBClassType rightClass
            ? string.Equals(leftClass.Symbol.Uri.AbsoluteUri, rightClass.Symbol.Uri.AbsoluteUri, StringComparison.Ordinal)
            : left == right;

    /// <summary>
    /// Helper function to twist the rules exactly in the same manner they're specified.
    /// </summary>
    /// <param name="source">The let-coercion <em>source</em> data type.</param>
    /// <param name="exceptTypeKinds">The kinds of type to exclude from the rule match.</param>
    /// <returns>
    /// <c>true</c> if the <c>source</c> type is not a kind of any of the <c>exceptTypeKinds</c>.
    /// </returns>
    /// <remarks>
    /// 👉 Matches by <em>kind</em> (<see cref="Type.IsInstanceOfType(object?)"/>), not by value equality:
    /// a parameterized type like <see cref="VBFixedSizeArrayType"/> has infinitely many instances (one
    /// per element type), so comparing against a single representative instance would only ever exclude
    /// that one specific element type.
    /// </remarks>
    private static bool AnyTypeExceptNonByteResizableArrayOr(VBType source, params Type[] exceptTypeKinds)
        => !IsNonByteResizableArray(source) && !exceptTypeKinds.Any(kind => kind.IsInstanceOfType(source));
    /// <summary>
    /// Helper function to twist the rules exactly in the same manner they're specified.
    /// </summary>
    /// <param name="type">The data type to control.</param>
    /// <returns>
    /// <c>true</c> if the <c>type</c> is a <c>VBResizableArrayType</c> but not a <c>VBResizableByteArrayType</c>.
    /// </returns>
    private static bool IsNonByteResizableArray(VBType type) => type is VBResizableArrayType and not VBResizableByteArrayType;
}