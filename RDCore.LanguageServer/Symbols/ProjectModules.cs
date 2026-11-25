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
        var taken = ModuleNameStaticSemantics.NamesTakenTwice(parsed.Select(module => module.Name));
        var modules = new List<ParsedModule>();
        var hosts = new Dictionary<string, DocClassType>(StringComparer.OrdinalIgnoreCase);
        foreach (var (document, moduleName, parseResult) in parsed)
        {
            workspaceRoot ??= SymbolUri.Root(document.WorkspaceRoot);
            if (taken.Contains(moduleName))
            {
                continue;
            }

            // module kind is never a parser input: it is read off the raw source, as the parsing pass reads it.
            var moduleKind = ParsingClientService.ModuleTypeOf(document);
            modules.Add(new ParsedModule(SymbolUri.Module(workspaceRoot, moduleName), moduleName, moduleKind, parseResult));
            if (document.Super is { } super)
            {
                hosts.TryAdd(moduleName, super);
            }
        }

        return (workspaceRoot, modules, hosts);
    }

    // the modules of a project whose name another module of it has, by their document, and the error each is in (MS-VBAL §4.2).
    public static IReadOnlyDictionary<string, (string ModuleName, VBCompileErrorInfo Error)> NameConflicts(
        IWorkspaceDocumentService documents, IParsingClientService parsing, string projectRoot, CancellationToken token)
    {
        var parsed = Parsed(documents, parsing, projectRoot, token);
        var taken = ModuleNameStaticSemantics.NamesTakenTwice(parsed.Select(module => module.Name));
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
}
