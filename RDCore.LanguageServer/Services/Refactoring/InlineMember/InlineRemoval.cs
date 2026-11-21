using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Services.Refactoring.InlineMember;

/// <summary>
/// Whether the member is removed once its calls are inlined, and why.
/// </summary>
internal enum InlineRemovalKind
{
    /// <summary>
    /// Every reference to the member is an inlined call, and nothing outside the workspace sees it: removed by default.
    /// </summary>
    Ticked,

    /// <summary>
    /// Every reference to the member in the workspace is an inlined call, and a project that references this one, or a host macro, can call it: a
    /// <c>Public</c> member of a standard module without <c>Option Private Module</c>, or of a class module (MS-VBAL §5.3.1.1). Kept by default.
    /// </summary>
    Unticked,

    /// <summary>
    /// A reference to the member is not an inlined call: a refused site, an <c>AddressOf</c>, a use of it that is not a call.
    /// </summary>
    ReferenceLeft,

    /// <summary>
    /// A name that does not resolve could mean the member.
    /// </summary>
    Unresolved,

    /// <summary>
    /// A string names the member where it is called by name (MS-VBAL §6.1.2.8.1.1).
    /// </summary>
    CalledByName,

    /// <summary>
    /// A late-bound call could reach the member.
    /// </summary>
    LateBound,
}

/// <summary>
/// Whether the member is removed once its calls are inlined: its removal, offered ticked or unticked, or why it is kept.
/// </summary>
/// <param name="Kind">Whether, and why.</param>
/// <param name="Module">The module of the member.</param>
/// <param name="Edit">The edit of <paramref name="Module"/> that removes the member, with its attributes and the comments above it, when it is offered.</param>
/// <param name="Location">Where the reference that keeps the member is, when one does.</param>
internal sealed record class InlineRemoval(InlineRemovalKind Kind, Uri Module, RefactorTextEdit? Edit = null, SourceLocation? Location = null)
{
    /// <summary>
    /// Whether the removal is offered, ticked or not.
    /// </summary>
    public bool IsOffered => Edit is not null;

    /// <summary>
    /// Whether the member is removed with its calls inlined at <paramref name="inlined"/>, and the edit that removes it.
    /// </summary>
    /// <param name="context">The workspace, whose modules are bound.</param>
    /// <param name="analysis">The analysis of the member's module.</param>
    /// <param name="callee">The member.</param>
    /// <param name="declaration">The member's declaration.</param>
    /// <param name="inlined">The name of the member at each call that is inlined, by its module.</param>
    public static InlineRemoval Of(
        RenameContext context, ExtractionModuleAnalysis analysis, Symbol callee, MemberDeclarationNode declaration,
        IReadOnlyCollection<(Uri Module, SourceRange Name)> inlined)
    {
        var module = analysis.Module;
        var found = NameOccurrences.Find(context, callee.Name, context.Modules);
        foreach (var unknown in found.Unknown)
        {
            if (unknown.Kind is OccurrenceKind.String)
            {
                if (RenameBinding.IsCalledBy(context, [callee], unknown))
                {
                    return new(InlineRemovalKind.CalledByName, module, Location: new(unknown.Module, unknown.Range));
                }

                continue;
            }

            if (unknown.Kind is OccurrenceKind.Member || RenameBinding.ChainIndex(unknown.Scope, callee) < int.MaxValue)
            {
                return new(InlineRemovalKind.Unresolved, module, Location: new(unknown.Module, unknown.Range));
            }
        }

        if (found.Occurrences.FirstOrDefault(occurrence => RenameBinding.LateBoundMatchOf(context, [callee], occurrence) != RenameBinding.LateBoundMatch.None)
            is { Module: not null } lateBound)
        {
            return new(InlineRemovalKind.LateBound, module, Location: new(lateBound.Module, lateBound.Range));
        }

        // the member's own name in its body, a function's result, goes with it.
        var own = declaration.SourceLocation.Range;
        var sites = inlined.Select(site => (site.Module.AbsoluteUri, site.Name)).ToHashSet();
        if (found.Occurrences.FirstOrDefault(occurrence => occurrence.Binding == callee.SemanticId && occurrence.Kind is not OccurrenceKind.Declaration
                && !(occurrence.Module.AbsoluteUri == module.AbsoluteUri && own.Start <= occurrence.Range.Start && occurrence.Range.End <= own.End)
                && !sites.Contains((occurrence.Module.AbsoluteUri, occurrence.Range)))
            is { Module: not null } left)
        {
            return new(InlineRemovalKind.ReferenceLeft, module, Location: new(left.Module, left.Range));
        }

        var edit = MemberEdits.Remove(analysis.Text, MemberEdits.Extent(analysis.Parse, analysis.Text, declaration));
        var isClass = NameOccurrences.SymbolOf(context, new SemanticId(NameOccurrences.ModuleOf(callee.Uri))) is VBClassModuleSymbol;
        var isPrivateModule = (context.ScopeTree.ScopeFor(module).EnclosingModuleDirectives() ?? ModuleDirectives.None).PrivateModule;
        var isSeenOutside = declaration.AccessModifier is not (AccessModifier.Private or AccessModifier.Friend) && (isClass || !isPrivateModule);
        return new(isSeenOutside ? InlineRemovalKind.Unticked : InlineRemovalKind.Ticked, module, edit);
    }
}
