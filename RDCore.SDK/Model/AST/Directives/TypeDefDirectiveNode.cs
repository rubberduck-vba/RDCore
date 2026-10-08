using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Directives;

/// <summary>
/// A <c>DirectiveNode</c> representing a <c>Def&lt;Type&gt;</c> module directive.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> of the directive.</param>
/// <param name="Token">The <c>DefType</c> token as written (any casing) mapping to a specific <c>VBType</c> (per the semantics defined in MS-VBAL 5.2.2 Implicit Definition Directives).</param>
/// <param name="Mappings">The prefixing scheme defined by this directive.</param>
public record class TypeDefDirectiveNode(SyntaxNodeId Identity, SourceLocation Location, string Token, ImmutableArray<DefTypePrefixMapping> Mappings)
    : DirectiveNode(Identity, Location, [])
{
    private static readonly ImmutableDictionary<string, string> _typeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [Tokens.DefBool] = VBTypeNames.VBBoolean,
        [Tokens.DefByte] = VBTypeNames.VBByte,
        [Tokens.DefCur] = VBTypeNames.VBCurrency,
        [Tokens.DefDate] = VBTypeNames.VBDate,
        [Tokens.DefDbl] = VBTypeNames.VBDouble,
        [Tokens.DefInt] = VBTypeNames.VBInteger,
        [Tokens.DefLng] = VBTypeNames.VBLong,
        [Tokens.DefLngLng] = VBTypeNames.VBLongLong,
        [Tokens.DefLngPtr] = VBTypeNames.VBLongPtr,
        [Tokens.DefObj] = VBTypeNames.VBObject,
        [Tokens.DefSng] = VBTypeNames.VBSingle,
        [Tokens.DefStr] = VBTypeNames.VBString,
        [Tokens.DefVar] = VBTypeNames.VBVariant,
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The name of the type this directive gives to the names it covers (<c>Integer</c> for <c>DefInt</c>), or <see langword="null"/> for a token that is no <c>Def&lt;Type&gt;</c>.
    /// </summary>
    /// <remarks>
    /// A name rather than a <see cref="VBType"/>: <c>LongPtr</c> is a different type per environment, and it is the resolver that knows which one is bound.
    /// </remarks>
    public string? TypeName => _typeNames.TryGetValue(Token, out var name) ? name : null;

    /// <summary>
    /// Whether the implicit declared type of <paramref name="identifierName"/> is given by this directive (MS-VBAL 5.2.3.1.5).
    /// </summary>
    /// <param name="identifierName">The name of an entity that is declared without a type.</param>
    public bool Covers(string identifierName) => Mappings.Any(mapping => mapping.IsMatch(identifierName));

    /// <summary>
    /// The type this directive gives to the names it covers, with <see cref="VBUnknownType"/> for a token that is no <c>Def&lt;Type&gt;</c>.
    /// </summary>
    /// <param name="is64bit">Whether <c>DefLngPtr</c> is a 64-bit pointer.</param>
    public VBType GetVBType(bool is64bit) => TypeName switch
    {
        VBTypeNames.VBBoolean => VBBooleanType.TypeInfo,
        VBTypeNames.VBByte => VBByteType.TypeInfo,
        VBTypeNames.VBCurrency => VBCurrencyType.TypeInfo,
        VBTypeNames.VBDate => VBDateType.TypeInfo,
        VBTypeNames.VBDouble => VBDoubleType.TypeInfo,
        VBTypeNames.VBInteger => VBIntegerType.TypeInfo,
        VBTypeNames.VBLong => VBLongType.TypeInfo,
        VBTypeNames.VBLongLong => VBLongLongType.TypeInfo,
        VBTypeNames.VBLongPtr => is64bit ? VBLongPtrType_x64.TypeInfo : VBLongPtrType_x86.TypeInfo,
        VBTypeNames.VBObject => VBObjectType.TypeInfo,
        VBTypeNames.VBSingle => VBSingleType.TypeInfo,
        VBTypeNames.VBString => VBStringType.TypeInfo,
        VBTypeNames.VBVariant => VBVariantType.TypeInfo,

        _ => VBUnknownType.TypeInfo // illegal
    };
}
