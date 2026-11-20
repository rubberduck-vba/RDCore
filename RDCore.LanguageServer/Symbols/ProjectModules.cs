using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Workspace;
using RDCore.Symbols;

namespace RDCore.LanguageServer.Symbols;

// the modules of a project that have a parse, as a project is composed of them.
internal static class ProjectModules
{
    // every module of a project that has a parse, addressed under the project's root; the primary project's are addressed under the root of the project that
    // loaded them first, which is the primary project. What host document each document module is the code of goes with them, by the module's name.
    public static (Uri? WorkspaceRoot, List<ParsedModule> Modules, Dictionary<string, DocClassType> Hosts)
        Collect(IWorkspaceDocumentService documents, IParsingClientService parsing, string projectRoot, Uri? workspaceRoot, CancellationToken token)
    {
        var modules = new List<ParsedModule>();
        var hosts = new Dictionary<string, DocClassType>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents.GetProjectDocuments(projectRoot))
        {
            token.ThrowIfCancellationRequested();

            // a document of a server with no workspace is a project of its own, of which the host has no session.
            if (document.WorkspaceRoot.Length == 0 || !parsing.TryGetCached(document.Id.Uri.ToUri(), out var parseResult) || parseResult.SyntaxTree is null)
            {
                continue;
            }

            workspaceRoot ??= SymbolUri.Root(document.WorkspaceRoot);
            // the module's programmatic name is its Attribute VB_Name; the file name is the fallback.
            var moduleName = parseResult.SyntaxTree?.GetDeclaredName() ?? document.Name;
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
}
