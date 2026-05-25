using RDCore.SDK.Model.Symbols.Unbound;

namespace RDCore.SDK.Model.Symbols.Abstract;

public record class SymbolProperty<T>(string Name) { }

/// <summary>
/// Defines LSP-compliant extension properties for symbols. RDCore uses this to carry the <c>VB_Attribute</c> values associated with a symbol.
/// </summary>
public static class SymbolProperties
{
    /// <summary>
    /// The programmatic name of a <see cref="VBModuleSymbol"/>, as determined by the <c>VB_Name</c> attribute.
    /// </summary>
    public static readonly SymbolProperty<string> Name = new(nameof(Name));
    /// <summary>
    /// The value of the <c>VB_PredeclaredId</c> attribute of a <see cref="VBClassModuleSymbol"/>.
    /// </summary>
    public static readonly SymbolProperty<bool> PredeclaredId = new(nameof(PredeclaredId));
    /// <summary>
    /// The value of the <c>VB_Exposed</c> attribute of a <see cref="VBClassModuleSymbol"/>
    /// </summary>
    public static readonly SymbolProperty<bool> Exposed = new(nameof(Exposed));
    /// <summary>
    /// The value of the <c>VB_UserMemId</c> attribute of a <see cref="VBTypeMemberSymbol"/>
    /// </summary>
    public static readonly SymbolProperty<int> MemberId = new(nameof(MemberId));
    /// <summary>
    /// A small documentation string about this symbol.
    /// </summary>
    /// <remarks>
    /// Provided via <c>VB_Description</c> attributes.
    /// </remarks>
    public static readonly SymbolProperty<string> DocString = new(nameof(DocString));
    /// <summary>
    /// A metadata flag that is used for controlling the member behavior.
    /// </summary>
    /// <remarks>
    /// Provided via <c>VB_UserMemId</c> attributes; unique for each member of a given module, with value 0 denoting the default member.
    /// </remarks>
    public static readonly SymbolProperty<int> UserMemId = new(nameof(UserMemId));
    /// <summary>
    /// A metadata flag that is used for controlling the member behavior.
    /// </summary>
    /// <remarks>
    /// Provided via <c>VB_MemberFlags</c> attributes, with a handful of useful "magic" values.
    /// </remarks>
    public static readonly SymbolProperty<int> MemberFlags = new(nameof(MemberFlags));
}
