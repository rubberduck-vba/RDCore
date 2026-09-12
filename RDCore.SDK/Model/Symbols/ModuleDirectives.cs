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
public readonly record struct ModuleDirectives(bool Explicit = false, bool Strict = false)
{
    /// <summary>
    /// The directives of a module that declares none of them explicitly.
    /// </summary>
    public static readonly ModuleDirectives None = new();
}
