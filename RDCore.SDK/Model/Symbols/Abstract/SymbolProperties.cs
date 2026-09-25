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
    /// Whether a variable is an <em>automatic instantiation variable</em> (<strong>MS-VBAL §2.5.1</strong>): one
    /// declared with an <c>As New</c> clause (<strong>§5.2.3.1.1</strong>), or the default instance variable of a
    /// predeclared class (<strong>§5.2.4.1.2</strong>, declared "as if" <c>As New</c>). Each time its content is
    /// accessed while its value is <c>Nothing</c>, a new instance of its class is created and stored in it. For an
    /// array variable, it is each dependent variable — each element — that is one.
    /// </summary>
    public static readonly SymbolProperty<bool> AutoInstantiated = new(nameof(AutoInstantiated));
    /// <summary>
    /// The value of the <c>VB_Exposed</c> attribute of a <see cref="VBClassModuleSymbol"/>
    /// </summary>
    public static readonly SymbolProperty<bool> Exposed = new(nameof(Exposed));
    /// <summary>
    /// The value of the <c>VB_Creatable</c> attribute of a <see cref="VBClassModuleSymbol"/> — whether
    /// <c>New</c> can instantiate it. <see cref="Symbol.GetProperty{T}"/> returns C#'s
    /// <c>default(bool)</c> (<c>false</c>) when unset, the opposite of VBE's own default (creatable,
    /// for a class that declares no such attribute) — a builder that constructs a
    /// <see cref="VBClassModuleSymbol"/> MUST set this explicitly, never leave it to the read-site
    /// default. <c>WorkspaceSymbolResolver.Compose</c> does; other builders that synthesize a bare
    /// class module symbol without parsing its attributes (e.g. <c>ProjectSymbolProvider</c>) don't yet.
    /// </summary>
    public static readonly SymbolProperty<bool> Creatable = new(nameof(Creatable));
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
