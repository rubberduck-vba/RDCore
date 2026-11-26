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
    // loaded them first, which is the primary project. The parts of one name are one module (ModuleAssembly, RD-VBAL §5.1); a module its parts make
    // invalid, two plain parts of one name among them, is none of them (Refused). What host document each document module is the code of goes with them, by
    // the module's name.
    public static (Uri? WorkspaceRoot, List<ParsedModule> Modules, Dictionary<string, DocClassType> Hosts)
        Collect(IWorkspaceDocumentService documents, IParsingClientService parsing, string projectRoot, Uri? workspaceRoot, CancellationToken token)
    {
        var parsed = Parsed(documents, parsing, projectRoot, token);
        var hosts = new Dictionary<string, DocClassType>(StringComparer.OrdinalIgnoreCase);
        if (parsed.Count == 0)
        {
            return (workspaceRoot, [], hosts);
        }

        workspaceRoot ??= SymbolUri.Root(parsed[0].Document.WorkspaceRoot);
        var (modules, _) = ParsedModule.Assemble(workspaceRoot, PartsOf(parsed));
        foreach (var (document, name, _) in parsed)
        {
            // a host document is the code of the file's own module.
            if (document.Super is { } super
                && modules.Any(module => module.Document == document.Id.Uri.ToUri() && string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                hosts.TryAdd(name, super);
            }
        }

        return (workspaceRoot, [.. modules], hosts);
    }

    // the errors of each module of a project that its parts make invalid (ModuleAssembly, RD-VBAL §5.1), two modules of one name among them (MS-VBAL §4.2),
    // by the document each error is in: none of the module's parts is defined in the host.
    public static IReadOnlyDictionary<string, IReadOnlyList<(string ModuleName, VBCompileErrorInfo Error)>> Refused(
        IWorkspaceDocumentService documents, IParsingClientService parsing, string projectRoot, CancellationToken token)
        => ModuleAssembly.Of(PartsOf(Parsed(documents, parsing, projectRoot, token))).Refused
            .SelectMany(module => module.Errors.Select(error => (module.Name, Error: error)))
            .GroupBy(each => each.Error.Location.Uri.AbsoluteUri)
            .ToDictionary(file => file.Key, file => (IReadOnlyList<(string, VBCompileErrorInfo)>)[.. file]);

    // the parts the documents are the text of: each one's own module, named by its Attribute VB_Name or else its file, of the kind its raw source says (never
    // a parser input, as the parsing pass reads it), then each of its blocks'.
    private static IEnumerable<ModulePart> PartsOf(List<(WorkspaceDocument Document, string Name, ModuleParseResult Parse)> parsed)
        => parsed.SelectMany(each => ParsedModules.PartsOf(each.Document.Id.Uri.ToUri(), each.Parse, each.Document.Name,
            ParsingClientService.ModuleTypeOf(each.Document)));

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
