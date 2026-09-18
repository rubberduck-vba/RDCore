using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Complex;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// The default instance variable of a class module whose <c>VB_PredeclaredId</c> attribute is
/// <c>True</c> (<strong>MS-VBAL §5.2.4.1.2</strong>): a variable named after the class, declared as that
/// class, created with module extent as if declared <c>As New</c> — that is, an automatic instantiation
/// variable (<see cref="SymbolProperties.AutoInstantiated"/>, <strong>§2.5.1</strong>) — without any code
/// declaring it, so the class name itself can be used as if it were an automatic instantiation variable,
/// the way <c>UserForm1.Show</c> names a form's default instance.
/// </summary>
/// <remarks>
/// A class module never binds as a name in the default binding context: its predeclared instance does
/// (<c>ISymbolResolver.ResolveValue</c>). The class itself is still what an <c>As</c> clause or the
/// operand of <c>New</c> names, in the type binding context. The variable is global rather than local to
/// its class module — <strong>RD-VBAL §3.1.1.5</strong> calls it a <em>global auto-object</em>.
/// <para>
/// It is invalid for this variable to be the target of a <c>Set</c> assignment
/// (<strong>§5.2.4.1.2</strong>); <c>StatementStaticSemanticsEvaluator</c> reports it.
/// </para>
/// <para>
/// Only the static side is modeled: the variable's declared type. That the variable is never
/// <c>Nothing</c> — it is re-created the next time it is referred to — is a run-time concern.
/// </para>
/// </remarks>
public sealed record class VBPredeclaredInstanceSymbol
    : BoundTypedSymbol
{
    /// <summary>
    /// Creates the default instance variable of <paramref name="classModule"/>.
    /// </summary>
    /// <param name="classModule">The class module this is the default instance of.</param>
    public VBPredeclaredInstanceSymbol(VBClassModuleSymbol classModule)
        : base(
            classModule.WorkspaceRoot, StaticSymbol.GlobalUri, classModule.Name, ScopeKind.Global, SymbolKindExt.Variable,
            SourceRange.Empty, SourceRange.Empty, VBClassType.FromClassModule(classModule))
    {
        ClassModule = classModule;
        Properties = Properties.SetItem(SymbolProperties.AutoInstantiated, true);
    }

    /// <summary>
    /// The class module this is the default instance of.
    /// </summary>
    public VBClassModuleSymbol ClassModule { get; init; }
}
