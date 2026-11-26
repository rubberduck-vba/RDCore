using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics;
using System.Collections.Concurrent;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// The branches of each module's <c>#If</c> blocks that are not live, as the environment host evaluated them when the module's symbols were last defined: what
/// a check of another parse of the module, an edit a refactoring would make, reads as not part of the module.
/// </summary>
/// <remarks>
/// MS-VBAL §3.4: the branches that are not live are not part of the module. The host evaluates the directives with the project's constants, which the
/// language server does not; it says which branches are dead of the parse it was sent, and a branch is known again in another parse by its ordinal, for as long
/// as the document reads as it did then and the other parse has the blocks of the same shape (<see cref="ConditionalCompilationBlocks.HasShapeOf"/>). Otherwise
/// nothing is known, and a check reads every branch as an alternative of the others.
/// </remarks>
internal sealed class ModuleLiveBranches(IWorkspaceDocumentService documents, IParsingClientService parsing)
{
    // by document: the version of the text the host was sent, its blocks, and the ordinals of the branches that are dead of them.
    private readonly ConcurrentDictionary<Uri, (int Version, ConditionalCompilationBlocks Blocks, IReadOnlyList<(int Block, int Branch)> Dead)> _modules
        = new();

    /// <summary>
    /// Records what the host said is dead of a parse of a module it was sent, when that parse is of the document as it reads now; forgets what it knew of the
    /// document otherwise.
    /// </summary>
    /// <param name="sent">The parse the host defined the module's symbols from.</param>
    /// <param name="dead">The branches the host evaluated as not live (<c>DefineSymbolsResult.DeadBranches</c>).</param>
    public void Record(ModuleParseResult sent, IReadOnlyList<SourceLocation> dead)
    {
        if (sent.SyntaxTree?.SourceLocation.Uri is not { } document)
        {
            return;
        }

        if (!documents.TryGetDocument(document, out var current)
            || !parsing.TryGetCached(document, current.Version, out var parse) || !ReferenceEquals(parse, sent))
        {
            _modules.TryRemove(document, out _);
            return;
        }

        var blocks = ConditionalCompilationBlocks.Of(sent.PrecompilerTrivia);
        _modules[document] = (current.Version, blocks, blocks.DeadOrdinals(dead));
    }

    /// <summary>
    /// Whether a location of a parse of a module is in a branch that is not live; <see langword="null"/> when that is not known: the host evaluated no parse of
    /// the document as it reads now, or the parse has other blocks than the one it evaluated.
    /// </summary>
    /// <param name="checkedParse">The parse that is checked: of the document's text, or of an edit of it.</param>
    public Func<SourceLocation, bool>? DeadTestFor(ModuleParseResult checkedParse)
    {
        if (checkedParse.SyntaxTree?.SourceLocation.Uri is not { } document
            || !_modules.TryGetValue(document, out var recorded)
            || !documents.TryGetDocument(document, out var current) || current.Version != recorded.Version)
        {
            return null;
        }

        var blocks = ConditionalCompilationBlocks.Of(checkedParse.PrecompilerTrivia);
        return blocks.HasShapeOf(recorded.Blocks) ? blocks.DeadTest(recorded.Dead) : null;
    }
}
