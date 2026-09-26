using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;

namespace RDCore.SDK.Runtime.StdLib;

/// <summary>
/// The symbols of the standard library and of the objects the language environment provides
/// globally — everything a name in VBA source can resolve to without the workspace declaring it.
/// </summary>
/// <remarks>
/// Without these, every standard-library name is unbound, and an unbound name in a module with no
/// <c>Option Explicit</c> is a declaration (<strong>MS-VBAL §5.6.10</strong>) — so <c>Debug.Print x</c>
/// would quietly declare a local called <c>Debug</c>. Resolution is what stops that, which is why this
/// exists before anything here is callable.
/// <para>
/// The standard library itself is read off the declarations that define it, by
/// <see cref="StdLibSymbolReader"/>, and is provided unconditionally: every VBA project has it whether
/// or not a <c>.rdproj</c> mentions it (<strong>RD-VBAL §6.1</strong>), so there is nothing to opt into
/// and nothing to reference.
/// </para>
/// <para>
/// <c>Debug</c> is <em>synthesized</em> here instead, because nothing declares it: it is not in
/// MS-VBAL's standard library at all (§6.1 has no <c>Debug</c>), it is provided by the host's own
/// development environment, and it has exactly two members.
/// </para>
/// </remarks>
public sealed class StdLibSymbolProvider : ISymbolProvider
{
    /// <summary>
    /// The name of the synthetic module every global the language environment provides hangs off.
    /// </summary>
    /// <remarks>
    /// A leading underscore is not a legal VBA identifier, so no source can name this module and no
    /// workspace module can collide with it — which is the whole reason for the spelling. It is a
    /// standard module, so its members are promoted to the project scope and resolve unqualified, the
    /// way a standard library's members do.
    /// <para>
    /// It also gives a global object's <em>class</em> somewhere to be parented that is not the global
    /// scope itself. That matters: a predeclared instance is named after its class, so a class parented
    /// to the global scope has the very same <c>Uri</c> as its own instance, and the name then resolves
    /// to neither of them.
    /// </para>
    /// </remarks>
    public const string GlobalModuleName = "_Global";

    /// <summary>
    /// The name of the object the development environment exposes for diagnostic output.
    /// </summary>
    public const string DebugObjectName = "Debug";

    /// <summary>
    /// <c>Debug.Print</c>: writes an output list to the environment's own output
    /// (<strong>MS-VBAL §5.4.5.8</strong>'s rules, against no file).
    /// </summary>
    public const string PrintMemberName = "Print";

    /// <summary>
    /// <c>Debug.Assert</c>: enters break mode when its expression is <c>False</c>.
    /// </summary>
    public const string AssertMemberName = "Assert";

    private readonly Uri _workspaceRoot;

    /// <summary>
    /// Creates the provider.
    /// </summary>
    /// <param name="workspaceRoot">
    /// The workspace the symbols are addressed under. Library symbols are not <em>of</em> the
    /// workspace, but every <see cref="Symbol"/> is addressed relative to one, so they share its root
    /// and hang off the global scope rather than off a module.
    /// </param>
    public StdLibSymbolProvider(Uri workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
    }

    /// <inheritdoc/>
    public IEnumerable<Symbol> ProvideSymbols()
    {
        var globalModule = new VBStandardModuleSymbol(_workspaceRoot, _workspaceRoot, GlobalModuleName);
        yield return globalModule;

        // The class is built but not yielded, and the difference matters. `_Global` is a standard
        // module, so anything parented to it is promoted to the project scope — and a class that is
        // also a global declaration, alongside an instance named after it, gives the one name three
        // candidates across two tiers, which resolves to none of them. Nothing needs it in the tree:
        // the instance carries it (VBPredeclaredInstanceSymbol.ClassModule), its members ride on the
        // class type's own default interface, which is how a member access binds them, and VBA has no
        // nameable `Debug` type for a type reference to want in the first place.
        //
        // What the module IS for: giving the class a parent that is not the global scope. A predeclared
        // instance is named after its class, so a class parented to the global scope has the very same
        // Uri as its own instance — the same name resolving to two identities that are indistinguishable.
        var debugClass = DebugClass(globalModule.Uri);

        // what `Debug` resolves to in the default binding context (MS-VBAL §5.2.4.1.2 — the same shape
        // as a VB_PredeclaredId class module's default instance).
        yield return new VBPredeclaredInstanceSymbol(debugClass);

        // MS-VBAL §6.1: the standard library, read off the SDK declarations that define it. The
        // declaring assembly is this one, and is found through a type of it rather than named, so that
        // a component with no reference to the concrete library still gets the symbols.
        foreach (var symbol in new StdLibSymbolReader(_workspaceRoot, globalModule.Uri).Read(typeof(StdLibSymbolProvider).Assembly))
        {
            yield return symbol;
        }
    }

    private VBClassModuleSymbol DebugClass(Uri globalModuleUri)
    {
        // it has a default instance and cannot be instantiated: `New Debug` names nothing.
        var debugClass = (VBClassModuleSymbol)new VBClassModuleSymbol(_workspaceRoot, globalModuleUri, DebugObjectName)
            .With(SymbolProperties.PredeclaredId, true)
            .With(SymbolProperties.Creatable, false);

        debugClass = debugClass with
        {
            Members = ImmutableArray.Create<VBTypeMemberSymbol>(PrintMember(debugClass.Uri), AssertMember(debugClass.Uri)),
        };
        // a pure function of Members, and fixed the moment they are known — same ordering the
        // workspace's own class modules follow.
        return debugClass with { DefaultInterfaceMembers = VBClassType.FromClassModule(debugClass).Members };
    }

    /// <remarks>
    /// <c>Debug.Print</c>'s output list is a grammar construct of its own
    /// (<strong>MS-VBAL §5.4.5.8.1</strong>), not an argument list — <c>Spc</c>, <c>Tab</c> and the
    /// <c>;</c>/<c>,</c> separators are not expressions that could be passed to a parameter. The
    /// <c>ParamArray</c> is what makes the member resolvable and arity-correct for a caller; the
    /// statement's own semantics read the output list off the syntax instead.
    /// </remarks>
    private VBTypeMemberSymbol PrintMember(Uri classUri)
    {
        var print = new VBProcedureMemberSymbol(
            _workspaceRoot, classUri, PrintMemberName, ScopeKind.Instance, SymbolKindExt.Procedure,
            VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

        return print with
        {
            Parameters =
            [
                new ParamArrayParameterSymbol(
                    _workspaceRoot, print.Uri, "OutputList", SourceRange.Empty, SourceRange.Empty, ParameterKind.ExplicitByVal),
            ],
        };
    }

    private VBTypeMemberSymbol AssertMember(Uri classUri)
    {
        var assert = new VBProcedureMemberSymbol(
            _workspaceRoot, classUri, AssertMemberName, ScopeKind.Instance, SymbolKindExt.Procedure,
            VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

        return assert with
        {
            Parameters =
            [
                new VBParameterSymbol(
                    _workspaceRoot, assert.Uri, "Expression", SourceRange.Empty, SourceRange.Empty,
                    ParameterKind.ExplicitByVal, VBBooleanType.TypeInfo),
            ],
        };
    }
}
