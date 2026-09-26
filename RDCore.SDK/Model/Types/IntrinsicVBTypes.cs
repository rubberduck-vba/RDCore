using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// The MS-VBAL 3.3.2 reserved data-type names and 3.3.1 type-declaration characters, each mapped to
/// its singleton <see cref="VBType"/>. This is the intrinsic-only fallback every component shares
/// when binding a declared type from a bare name without a project or library resolver — the
/// language server's AST symbol provider and the environment host's <c>rdcore/host/symbols/define</c>
/// handler resolve intrinsics identically from here, so a type that is <c>VBUnknownType</c> on one
/// side is <c>VBUnknownType</c> on the other.
/// </summary>
/// <remarks>
/// <c>LongPtr</c> is deliberately absent: its width depends on the host bitness
/// (<c>IRuntimeEnvironmentProfile.Is64Bit</c>), so a resolver that knows the profile must bind it.
/// </remarks>
public static class IntrinsicVBTypes
{
    private static readonly ImmutableDictionary<string, VBType> _byName = new Dictionary<string, VBType>(StringComparer.OrdinalIgnoreCase)
    {
        [VBTypeNames.VBBoolean] = VBBooleanType.TypeInfo,
        [VBTypeNames.VBByte] = VBByteType.TypeInfo,
        [VBTypeNames.VBCurrency] = VBCurrencyType.TypeInfo,
        [VBTypeNames.VBDate] = VBDateType.TypeInfo,
        [VBTypeNames.VBDecimal] = VBDecimalType.TypeInfo,
        [VBTypeNames.VBDouble] = VBDoubleType.TypeInfo,
        [VBTypeNames.VBInteger] = VBIntegerType.TypeInfo,
        [VBTypeNames.VBLong] = VBLongType.TypeInfo,
        [VBTypeNames.VBLongLong] = VBLongLongType.TypeInfo,
        [VBTypeNames.VBObject] = VBObjectType.TypeInfo,
        [VBTypeNames.VBSingle] = VBSingleType.TypeInfo,
        [VBTypeNames.VBString] = VBStringType.TypeInfo,
        [VBTypeNames.VBVariant] = VBVariantType.TypeInfo,
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly ImmutableDictionary<string, VBType> _byTypeHint = new Dictionary<string, VBType>
    {
        ["%"] = VBIntegerType.TypeInfo,
        ["&"] = VBLongType.TypeInfo,
        ["^"] = VBLongLongType.TypeInfo,
        ["!"] = VBSingleType.TypeInfo,
        ["#"] = VBDoubleType.TypeInfo,
        ["@"] = VBCurrencyType.TypeInfo,
        ["$"] = VBStringType.TypeInfo,
    }.ToImmutableDictionary();

    /// <summary>
    /// Resolves an intrinsic type by its reserved name (case-insensitive).
    /// </summary>
    public static bool TryResolve(string typeName, [NotNullWhen(true)] out VBType? type)
        => _byName.TryGetValue(typeName, out type);

    /// <summary>
    /// Resolves the intrinsic type named by an MS-VBAL 3.3.1 type-declaration character
    /// (<c>%</c>, <c>&amp;</c>, <c>^</c>, <c>!</c>, <c>#</c>, <c>@</c>, <c>$</c>).
    /// </summary>
    public static bool TryResolveTypeHint(string typeHint, [NotNullWhen(true)] out VBType? type)
        => _byTypeHint.TryGetValue(typeHint, out type);

    private static readonly ImmutableDictionary<Type, VBType> _byValueType = new Dictionary<Type, VBType>
    {
        [typeof(VBBooleanValue)] = VBBooleanType.TypeInfo,
        [typeof(VBByteValue)] = VBByteType.TypeInfo,
        [typeof(VBCurrencyValue)] = VBCurrencyType.TypeInfo,
        [typeof(VBDateValue)] = VBDateType.TypeInfo,
        [typeof(VBDecimalValue)] = VBDecimalType.TypeInfo,
        [typeof(VBDoubleValue)] = VBDoubleType.TypeInfo,
        [typeof(VBIntegerValue)] = VBIntegerType.TypeInfo,
        [typeof(VBLongValue)] = VBLongType.TypeInfo,
        [typeof(VBLongLongValue)] = VBLongLongType.TypeInfo,
        [typeof(VBObjectValue)] = VBObjectType.TypeInfo,
        [typeof(VBSingleValue)] = VBSingleType.TypeInfo,
        [typeof(VBStringValue)] = VBStringType.TypeInfo,
        [typeof(VBVariantValue)] = VBVariantType.TypeInfo,
    }.ToImmutableDictionary();

    /// <summary>
    /// Resolves the intrinsic type whose run-time values are of the given <see cref="VBTypedValue"/>
    /// implementation - the reverse of <see cref="VBType.CreateValue"/>.
    /// </summary>
    /// <remarks>
    /// This is how a declaration written in C# states an intrinsic VBA type: naming the value a member
    /// produces or a parameter accepts names the type, without restating it. The standard-library
    /// interfaces in <see cref="RDCore.SDK.Runtime.Abstract.StdLib"/> are declared that way.
    /// <para>
    /// The mapping covers exactly the intrinsics a declaration can name, which is the same set
    /// <see cref="TryResolve"/> resolves by name. The values that have no declarable type - a
    /// <see cref="VBEmptyValue"/>, a <see cref="VBMissingValue"/>, a <see cref="VBErrorValue"/> - are
    /// subtypes a <c>Variant</c> holds rather than types anything is declared as, and resolve here no
    /// more than <c>Dim x As Empty</c> compiles. <c>LongPtr</c> is absent for the same reason it is
    /// absent by name: its width depends on the host bitness.
    /// </para>
    /// </remarks>
    /// <param name="valueType">The <see cref="VBTypedValue"/> implementation to resolve the type of.</param>
    /// <param name="type">The intrinsic type whose values are of that implementation.</param>
    public static bool TryResolveValueType(Type valueType, [NotNullWhen(true)] out VBType? type)
        => _byValueType.TryGetValue(valueType, out type);
}
