namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// The resolved module-level directives a <see cref="VBModuleSymbol"/> was declared under — the
/// <c>Option ...</c> statements (<strong>MS-VBAL §5.2.1</strong>) and <c>Attribute VB_...</c>
/// declarations (<strong>MS-VBAL §4.2</strong>) a static-semantics pass needs to know about, plus
/// RD-VBA's own annotation-driven dials (<c>Option Strict</c>).
/// </summary>
/// <remarks>
/// Named <em>Directives</em>, not <em>Options</em>: MS-VBAL's own <c>Option</c> statements are only
/// one source feeding this — <c>Attribute</c> declarations (<c>VB_Name</c>, <c>VB_PredeclaredId</c>,
/// …) and RD-VBA annotations belong here too, as this grows one property at a time. A consumer that
/// needs a new directive adds a property to this record; nothing that carries a
/// <see cref="ModuleDirectives"/> — <see cref="VBModuleSymbol"/>, <see cref="LexicalScope"/>,
/// <c>StaticEvaluationContext</c> — ever changes shape because of it.
/// </remarks>
/// <param name="Explicit">
/// Whether the module declares <c>Option Explicit</c> (<strong>MS-VBAL §5.2.1.3</strong>) — an
/// unresolved <c>SimpleName</c> is a compile error under this module, not a deferred/inferred type.
/// </param>
/// <param name="Strict">
/// Whether the module carries RD-VBA's <c>'@OptionStrict</c> annotation — turns select semantic
/// flags that stay legal under plain <c>Option Explicit</c> into compile errors.
/// </param>
/// <param name="Compare">
/// The comparison mode of the module (<strong>MS-VBAL §5.2.1.1</strong>): how the relational operators compare <c>String</c> values in it.
/// <see cref="OptionCompare.Binary"/> unless the module declares an <c>Option Compare</c> directive.
/// </param>
/// <param name="Base">
/// The lower bound an array dimension declared without one takes (<strong>MS-VBAL §5.2.1.2</strong>):
/// <c>0</c> unless the module declares <c>Option Base 1</c>. It is what <c>Dim a(10)</c> and
/// <c>ReDim a(10)</c> mean by their absent lower bound, so it is a run-time dial and not only a static one.
/// </param>
/// <param name="PrivateModule">
/// Whether the module declares <c>Option Private Module</c> (<strong>MS-VBAL §5.2.1.4</strong>): the module is accessible only within the project that
/// encloses it, and the meaning of <c>Public</c> for the entities it declares is bounded by that. A module without it is accessible to the projects that
/// reference its project as well.
/// 🚧 TODO nothing consumes it yet: it is the fact, carried where the module is, for the resolution of a name across projects to read once it decides what
/// the platform means by a project reference.
/// </param>
public readonly record struct ModuleDirectives(
    bool Explicit = false, bool Strict = false, OptionCompare Compare = OptionCompare.Binary, int Base = 0, bool PrivateModule = false)
{
    /// <summary>
    /// The directives of a module that declares none of them explicitly.
    /// </summary>
    public static readonly ModuleDirectives None = new();
}
