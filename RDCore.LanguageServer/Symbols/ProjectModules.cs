using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Workspace;
using RDCore.Symbols;

namespace RDCore.LanguageServer.Symbols;

// the modules of a project that have a parse, as a project is composed of them.
internal static class ProjectModules
{
    // every module of a project that has a parse, addressed under the project's root; the primary project's are addressed under the root of the project that
    // loaded them first, which is the primary project. What host document each document module is the code of goes with them, by the module's name. A module
    // whose name another module of the project has is none of them (NameConflicts): two would have one address.
    public static (Uri? WorkspaceRoot, List<ParsedModule> Modules, Dictionary<string, DocClassType> Hosts)
        Collect(IWorkspaceDocumentService documents, IParsingClientService parsing, string projectRoot, Uri? workspaceRoot, CancellationToken token)
    {
        var parsed = Parsed(documents, parsing, projectRoot, token);
        var taken = ModuleNameStaticSemantics.NamesTakenTwice(NamesOf(parsed));
        var modules = new List<ParsedModule>();
        var hosts = new Dictionary<string, DocClassType>(StringComparer.OrdinalIgnoreCase);
        foreach (var (document, _, parseResult) in parsed)
        {
            workspaceRoot ??= SymbolUri.Root(document.WorkspaceRoot);
            // the document's modules: its own, named by its Attribute VB_Name or else its file, of the kind its raw source says (never a parser input, as the
            // parsing pass reads it), then each of its blocks', each left out when another module has its name. A host document is the code of the file's own
            // module.
            var its = ParsedModules.Of(workspaceRoot, document.Id.Uri.ToUri(), parseResult, document.Name, ParsingClientService.ModuleTypeOf(document));
            modules.AddRange(its.Where(module => !taken.Contains(module.Name)));
            if (document.Super is { } super && !taken.Contains(its[0].Name))
            {
                hosts.TryAdd(its[0].Name, super);
            }
        }

        return (workspaceRoot, modules, hosts);
    }

    // the modules of a project whose name another module of it has, a block's included, by their document, and the error each is in (MS-VBAL §4.2): a
    // file's own module only, which the error is said in.
    public static IReadOnlyDictionary<string, (string ModuleName, VBCompileErrorInfo Error)> NameConflicts(
        IWorkspaceDocumentService documents, IParsingClientService parsing, string projectRoot, CancellationToken token)
    {
        var parsed = Parsed(documents, parsing, projectRoot, token);
        var taken = ModuleNameStaticSemantics.NamesTakenTwice(NamesOf(parsed));
        return parsed.Where(module => taken.Contains(module.Name)).ToDictionary(
            module => module.Document.Id.Uri.ToUri().AbsoluteUri,
            module => (module.Name, ModuleNameStaticSemantics.NameTaken(module.Parse.SyntaxTree!, module.Name)));
    }

    private static List<(WorkspaceDocument Document, string Name, ModuleParseResult Parse)> Parsed(
        IWorkspaceDocumentService documents, IParsingClientService parsing, string projectRoot, CancellationToken token)
    {
        var parsed = new List<(WorkspaceDocument Document, string Name, ModuleParseResult Parse)>();
        foreach (var document in documents.GetProjectDocuments(projectRoot))
        {
            token.ThrowIfCancellationRequested();

            // a document of a server with no workspace is a project of its own, of which the host has no session.
            if (document.WorkspaceRoot.Length == 0 || !parsing.TryGetCached(document.Id.Uri.ToUri(), out var parseResult) || parseResult.SyntaxTree is null)
            {
                continue;
            }

            // the module's programmatic name is its Attribute VB_Name; the file name is the fallback.
            parsed.Add((document, parseResult.SyntaxTree.GetDeclaredName() ?? document.Name, parseResult));
        }

        return parsed;
    }

    // the name of each module of the parsed documents: each one's own, then its blocks'.
    private static IEnumerable<string> NamesOf(List<(WorkspaceDocument Document, string Name, ModuleParseResult Parse)> parsed)
        => parsed.SelectMany(module => module.Parse.BlockModules.Select(block => block.Name).Prepend(module.Name));
}
