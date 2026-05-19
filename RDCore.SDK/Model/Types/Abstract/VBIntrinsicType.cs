using RDCore.SDK.Model.Types.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Abstract;

public abstract record class VBIntrinsicType : VBType
{
    private static readonly Lazy<ImmutableArray<VBType>> _intrinsicTypes = new(() =>
        [
            VBBooleanType.TypeInfo,
            VBByteType.TypeInfo,
            VBCurrencyType.TypeInfo,
            VBDateType.TypeInfo,
            VBDecimalType.TypeInfo,
            VBDoubleType.TypeInfo,
            VBEmptyType.TypeInfo,
            VBErrorType.TypeInfo,
            VBIntegerType.TypeInfo,
            VBLongType.TypeInfo,
            VBLongLongType.TypeInfo,
            VBNullType.TypeInfo,
            VBSingleType.TypeInfo,
            VBStringType.TypeInfo,
            VBObjectType.TypeInfo,
            VBVariantType.TypeInfo,
        ], LazyThreadSafetyMode.PublicationOnly);
    public static ImmutableArray<VBType> IntrinsicTypes => _intrinsicTypes.Value;

    public static bool TryResolve(string name, out VBType type)
    {
        type = IntrinsicTypes.SingleOrDefault(t => t.Name.Equals(name ?? Tokens.Variant, StringComparison.InvariantCultureIgnoreCase))!;
        return type != null;
    }

    protected VBIntrinsicType(string name, Type managedType)
        : base(managedType, name, isUserDefined: false) { }

    /// <summary>
    /// <c>true</c> for all intrinsic types that are valid in an <c>AsTypeClause</c> declaration.
    /// </summary>
    /// <remarks>
    /// Non-declarable types include <c>Decimal</c>, <c>Missing</c>, <c>Empty</c>, <c>Null</c>, and others.
    /// </remarks>
    public virtual bool IsDeclarable { get; } = true;

    /// <summary>
    /// Specifies a <em>type hint</em> character that can help typing literal values.
    /// </summary>
    public virtual char? TypeHintCharacter { get; }

    /// <summary>
    /// Specifies a <c>DefType</c> token that could really put this type system to the test.
    /// </summary>
    /// <remarks>
    /// Probably best to not implement this?
    /// </remarks>
    public virtual string? DefToken { get; }

    public override VBType[] ConvertsSafelyToTypes => [.. IntrinsicTypes
    .Except([VBObjectType.TypeInfo, VBDateType.TypeInfo, VBErrorType.TypeInfo, VBNullType.TypeInfo, VBEmptyType.TypeInfo])
    .Where(t => t.DefaultValue.Size >= DefaultValue.Size)];
}

public abstract record class VBIntrinsicType<T> : VBIntrinsicType
{
    protected VBIntrinsicType(string name) : base(name, typeof(T)) { }
}
