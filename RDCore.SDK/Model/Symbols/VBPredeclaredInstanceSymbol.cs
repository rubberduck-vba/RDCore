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
/// its class module — <strong>RD-VBAL §3.1.1.6</strong> calls it a <em>global auto-object</em>.
/// <para>
/// The variable can be the target of a <c>Set</c> assignment, as it can in MS-VBA, where <strong>MS-VBAL §5.2.4.1.2</strong> says it is invalid:
/// <c>Set Widget = Nothing</c> lets go of the object it holds, and the next reference to the variable creates another (<strong>RD-VBAL §5.2.4.1.2</strong>).
/// </para>
/// <para>
/// The variable is never <c>Nothing</c> when it is referred to: the reference creates the object, as it does for any automatic instantiation variable.
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
