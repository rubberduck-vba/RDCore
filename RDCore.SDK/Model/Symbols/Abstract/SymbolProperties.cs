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
    /// Whether a variable exists because something referred to its name and no declaration of it was written
    /// (<strong>MS-VBAL §5.6.10</strong>), rather than because the source declares it.
    /// </summary>
    /// <remarks>
    /// An implicit local says so by its <c>DeclaredBy</c>; an implicit <em>module-level</em> variable, which is what
    /// <see cref="ImplicitDeclarationScope.Module"/> declares, is the same kind of symbol as an explicit one and says so
    /// here. A later declaration pass, whose resolver already holds this variable, needs to tell it from a variable the
    /// source declares, or it would take its own earlier output for the declaration it is about to make.
    /// </remarks>
    public static readonly SymbolProperty<bool> ImplicitlyDeclared = new(nameof(ImplicitlyDeclared));
    /// <summary>
    /// The name of the library project a symbol belongs to — <c>VBA</c> for the standard library of a VBA
    /// environment — or unset for a symbol of the enclosing project.
    /// </summary>
    /// <remarks>
    /// The standard library's modules sit at the same tier of the scope tree as the workspace's own, which is what lets
    /// <c>Len</c> resolve unqualified. Which <em>project</em> a module is in is the one thing that tier cannot say, and
    /// the thing a project-qualified reference (<c>VBA.Strings.LenB</c>, <c>VBA.LenB</c>; <strong>MS-VBAL §5.6.12</strong>)
    /// needs to know. A library project's own <see cref="VBProjectSymbol"/> carries the same name, and a qualifier
    /// reaches the modules and members that carry it. The name is the environment's: a VB6 environment will call it
    /// something else.
    /// </remarks>
    public static readonly SymbolProperty<string> Library = new(nameof(Library));
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
    /// <summary>
    /// 🎯 The key identifying the <em>external</em> implementation a member dispatches to: a member whose
    /// code is not the workspace's, so no instruction list exists for it and
    /// <c>IExternalDispatcher</c> runs it instead.
    /// </summary>
    /// <remarks>
    /// Set by whatever contributed the symbol, because only that knows what the symbol stands for - the
    /// standard library's reader knows the declaration it read the member off, and nothing downstream could
    /// reconstruct it from a name. Absent on every symbol a workspace declares, which is what tells the two
    /// apart at the point of invocation.
    /// </remarks>
    public static readonly SymbolProperty<string> ExternalTarget = new(nameof(ExternalTarget));
}
